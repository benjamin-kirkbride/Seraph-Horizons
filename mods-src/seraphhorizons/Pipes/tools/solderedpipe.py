"""The soldered pipe: how copper and lead pipe is drawn, wherever it is drawn (UnifiedPipes).

Pipes and Power Expanded (ppex) draws a pipe as a square tube with an iron band and bolts round each
end, a flange: right for its iron and steel pipe, which is cast and banded. Copper and lead pipe is
soldered, so this module builds this mod's own copper and lead pipe: ppex's cross-section (a square
tube 6 voxels across, 1-voxel walls round a 4 x 4 bore, its block's 5..11, so it meets ppex's pipes
and fittings flush), no band and no bolts, and at each joint a thin bead of solder round the seam,
short and barely proud of the pipe (`JOINT_LENGTH`, `JOINT_SWELL`), in the solder's texture.

Two places draw it and both build it here, so they cannot drift apart:

* `make_pipe_shapes.py`: the shapes of ppex's straight, bend, T- and X-junction pipes in copper and
  lead (`shapes/block/pipes/soldered-*.json`), half a joint at each open end, so two pipes side by
  side make one whole joint over the block face between them;
* the rosser's drip pipes (`Rosser/tools/make_shape.py`), the same tube, joints, hub and texture
  mapping in that machine's frame.

Every box is axis-aligned, in voxels. A box's faces are drawn unless something covers them
(`cull_pressed`): a face pressed flat against other boxes' faces is left out, so nothing in a pipe
z-fights and nothing is drawn that cannot be seen. UVs map each face to a region of its texture the
face's own size from the texture's corner, one texel a voxel on ppex's 16-texel scale, as ppex's own
pipe and this mod's pipe section item do (`uv`). Stdlib only, deterministic.
"""

from __future__ import annotations

HALF = 3.0                                   # half the tube across: ppex's pipe, 6 voxels (its block's 5..11)
WALL = 1.0                                   # the wall: a 4 x 4 bore
# A joint is a thin bead of solder over the seam, as a sweated or wiped joint on pipe this size is: one
# ring, half on each pipe end. Half a joint runs JOINT_LENGTH along the pipe from its seam and stands
# JOINT_SWELL proud of the tube's faces, so a whole joint is 1.5 voxels long and 6.4 across (the pipe 6).
JOINT_LENGTH = 0.75
JOINT_SWELL = 0.2

BODY, SOLDER = "body", "solder"              # the two textures a pipe wears: its metal, and the joints' solder
# The solder: the game's lead solder (its ingot's texture), the tin-lead the joints are wiped with.
SOLDER_TEXTURE = "game:block/metal/ingot/leadsolder"

FACES = ("north", "east", "south", "west", "up", "down")
# each face's axis and side
FACE_NORMAL = {"west": (0, -1), "east": (0, 1), "down": (1, -1), "up": (1, 1), "north": (2, -1), "south": (2, 1)}
# which axes a face's u and v run along (the game's, as machinegen's FACE_UV_AXES)
FACE_UV_AXES = {"north": (0, 1), "south": (0, 1), "east": (2, 1), "west": (2, 1), "up": (0, 2), "down": (0, 2)}
# the two axes across a pipe along each axis: (u, v), v the one a ring's full-width plates face
ACROSS = {0: (2, 1), 1: (0, 2), 2: (0, 1)}
# the side of each axis by world direction
SIDE_NAME = {(0, -1): "west", (0, 1): "east", (1, -1): "down", (1, 1): "up", (2, -1): "north", (2, 1): "south"}
AXIS = {"x": 0, "y": 1, "z": 2}


class Box:
    """An axis-aligned box `lo`..`hi` (voxels) in one `role` (BODY or SOLDER), drawn on `faces`."""

    __slots__ = ("name", "lo", "hi", "role", "faces")

    def __init__(self, name, lo, hi, role):
        self.name, self.lo, self.hi, self.role = name, [float(v) for v in lo], [float(v) for v in hi], role
        self.faces = list(FACES)

    def size(self):
        return [self.hi[k] - self.lo[k] for k in range(3)]

    def moved(self, d):
        out = Box(self.name, [self.lo[k] + d[k] for k in range(3)], [self.hi[k] + d[k] for k in range(3)], self.role)
        out.faces = list(self.faces)
        return out


def _box(name, axis, a, b, centre, u_span, v_span, role):
    """A box from a to b along `axis`, over u_span and v_span (offsets from `centre`, the (u, v) of the
    pipe's middle) across it."""
    u, v = ACROSS[axis]
    lo, hi = [0.0] * 3, [0.0] * 3
    lo[axis], hi[axis] = min(a, b), max(a, b)
    lo[u], hi[u] = centre[0] + u_span[0], centre[0] + u_span[1]
    lo[v], hi[v] = centre[1] + v_span[0], centre[1] + v_span[1]
    return Box(name, lo, hi, role)


def _walls(name, axis, a, b, centre, outer, inner, role):
    """Four plates round a square section along `axis`: the two across v full width (`outer`), the
    two across u between them; each named for the side it faces."""
    u, v = ACROSS[axis]
    return [_box(f"{name}_{SIDE_NAME[(v, -1)]}", axis, a, b, centre, (-outer, outer), (-outer, -inner), role),
            _box(f"{name}_{SIDE_NAME[(v, 1)]}", axis, a, b, centre, (-outer, outer), (inner, outer), role),
            _box(f"{name}_{SIDE_NAME[(u, -1)]}", axis, a, b, centre, (-outer, -inner), (-inner, inner), role),
            _box(f"{name}_{SIDE_NAME[(u, 1)]}", axis, a, b, centre, (inner, outer), (-inner, inner), role)]


def tube(name, axis, a, b, centre):
    """A length of pipe along `axis` (0, 1, 2) from a to b, its middle at `centre` = (u, v) across it
    (`ACROSS`): four walls round the bore."""
    return _walls(name, axis, a, b, centre, HALF, HALF - WALL, BODY)


def half_joint(name, axis, seam, inward, centre):
    """Half a joint on a pipe along `axis`, from its seam at `seam` along the pipe the way `inward`
    (+1 or -1) says: a ring of solder round the tube, `JOINT_LENGTH` long, `JOINT_SWELL` proud."""
    return _walls(name, axis, seam, seam + inward * JOINT_LENGTH, centre, HALF + JOINT_SWELL, HALF, SOLDER)


def joint(name, axis, seam, centre):
    """A whole joint over a seam: both halves."""
    return half_joint(f"{name}a", axis, seam, -1, centre) + half_joint(f"{name}b", axis, seam, 1, centre)


def hub(name, centre, arms, primary=None):
    """Where pipes meet (a bend, a tee, a cross): a cube of the pipe's section about `centre` (x, y,
    z), its walls the tube's, open where an arm leaves it. `arms` are world directions ("north", ...);
    each arm's tube starts at the cube's face. The cube is a length of tube along `primary` (an arm's
    axis; by default one with arms both ways, else the first arm's), its walls cut where an arm leaves
    across it, and plugged at an end with no arm."""
    dirs = [FACE_NORMAL[a] for a in arms]
    if primary is None:
        both = [k for k in range(3) if (k, -1) in dirs and (k, 1) in dirs]
        primary = both[0] if both else dirs[0][0]
    p = primary
    u, v = ACROSS[p]
    c = (centre[u], centre[v])
    a, b = centre[p] - HALF, centre[p] + HALF
    i = HALF - WALL
    out = []
    # the walls across v: full width; an arm through one leaves a rim of four strips
    for side in (-1, 1):
        nm = f"{name}_{SIDE_NAME[(v, side)]}"
        vs = (-HALF, -i) if side < 0 else (i, HALF)
        if (v, side) in dirs:
            out += [_box(f"{nm}{k}", p, a0, b0, c, us, vs, BODY) for k, (a0, b0, us) in enumerate(
                ((a, a + WALL, (-HALF, HALF)), (b - WALL, b, (-HALF, HALF)), (a + WALL, b - WALL, (-HALF, -i)),
                 (a + WALL, b - WALL, (i, HALF))), 1)]
        else:
            out.append(_box(nm, p, a, b, c, (-HALF, HALF), vs, BODY))
    # the walls across u, between them; an arm through one leaves its two ends
    for side in (-1, 1):
        nm = f"{name}_{SIDE_NAME[(u, side)]}"
        us = (-HALF, -i) if side < 0 else (i, HALF)
        if (u, side) in dirs:
            out += [_box(f"{nm}1", p, a, a + WALL, c, us, (-i, i), BODY), _box(f"{nm}2", p, b - WALL, b, c, us, (-i, i), BODY)]
        else:
            out.append(_box(nm, p, a, b, c, us, (-i, i), BODY))
    # an end with no arm is plugged inside the walls
    for side, (a0, b0) in ((-1, (a, a + WALL)), (1, (b - WALL, b))):
        if (p, side) not in dirs:
            out.append(_box(f"{name}_{SIDE_NAME[(p, side)]}", p, a0, b0, c, (-i, i), (-i, i), BODY))
    return out


# ---------------------------------------------------------------- faces
def _face_rect(box, d):
    """Face `d` of `box`: (axis, plane, its rectangle over the other two axes in order)."""
    k, sgn = FACE_NORMAL[d]
    o = [a for a in range(3) if a != k]
    plane = box.hi[k] if sgn > 0 else box.lo[k]
    return k, plane, (box.lo[o[0]], box.hi[o[0]], box.lo[o[1]], box.hi[o[1]])


def _subtract(rects, cut, eps=1e-6):
    """Rectangles (x0, x1, y0, y1) less `cut`."""
    out = []
    cx0, cx1, cy0, cy1 = cut
    for x0, x1, y0, y1 in rects:
        if cx0 >= x1 - eps or cx1 <= x0 + eps or cy0 >= y1 - eps or cy1 <= y0 + eps:
            out.append((x0, x1, y0, y1))
            continue
        if cx0 > x0 + eps:
            out.append((x0, cx0, y0, y1))
        if cx1 < x1 - eps:
            out.append((cx1, x1, y0, y1))
        ix0, ix1 = max(x0, cx0), min(x1, cx1)
        if cy0 > y0 + eps:
            out.append((ix0, ix1, y0, cy0))
        if cy1 < y1 - eps:
            out.append((ix0, ix1, cy1, y1))
    return out


OPPOSITE = {"north": "south", "south": "north", "east": "west", "west": "east", "up": "down", "down": "up"}


def cull_pressed(boxes, eps=1e-6):
    """Leaves out every face pressed flat against other boxes: one wholly covered by the opposite
    faces of the boxes it touches (they are inside each other's bodies and can never be seen).
    Returns the boxes, changed in place."""
    keep = []
    for b in boxes:
        faces = []
        for d in b.faces:
            k, plane, rect = _face_rect(b, d)
            left = [rect]
            for other in boxes:
                if other is b or not left:
                    continue
                ok_, oplane, orect = _face_rect(other, OPPOSITE[d])
                if abs(oplane - plane) < eps:
                    left = _subtract(left, orect)
            if left:
                faces.append(d)
        keep.append(faces)
    for b, faces in zip(boxes, keep):
        b.faces = faces
    return boxes


def uv(d, size, scale=1.0):
    """A face's UVs: the region of its texture the face's own size, from the corner, `scale` texels
    a voxel (1 on ppex's 16-texel scale)."""
    a, b = FACE_UV_AXES[d]
    return [0.0, 0.0, round(abs(size[a]) * scale, 4), round(abs(size[b]) * scale, 4)]


def check(boxes):
    """No two boxes overlap, and no two drawn faces of different boxes lie in one plane facing the
    same way and overlap (they would z-fight). Returns the problems."""
    bad = []
    for i, a in enumerate(boxes):
        for b in boxes[i + 1:]:
            if all(min(a.hi[k], b.hi[k]) - max(a.lo[k], b.lo[k]) > 1e-6 for k in range(3)):
                bad.append(f"{a.name} and {b.name} overlap")
    faces = [(b, d) + _face_rect(b, d) for b in boxes for d in b.faces]
    for i, (a, da, ka, pa, ra) in enumerate(faces):
        for b, db, kb, pb, rb in faces[i + 1:]:
            if b is a or da != db or abs(pa - pb) > 1e-6:
                continue
            ox = min(ra[1], rb[1]) - max(ra[0], rb[0])
            oy = min(ra[3], rb[3]) - max(ra[2], rb[2])
            if ox > 1e-6 and oy > 1e-6:
                bad.append(f"{a.name} {da} and {b.name} {db} share a plane")
    return bad
