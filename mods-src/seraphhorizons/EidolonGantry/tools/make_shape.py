#!/usr/bin/env python3
"""Generate the eidolon gantry's shapes and rig (Python 3.11 stdlib only).

The gantry is where the player-built eidolon (../../Eidolon/README.md) is assembled, stage by stage,
and where it docks afterwards for repair and recharge: an open oak frame six blocks deep, five wide
and five and a half high, with iron plates, brackets and pegs, and a geared hand winch at the back
(two spur stages, 25 to 1, and a ratchet) whose chain runs up over one sheave on the hoist beam and
down to the body, its crank outside the frame on the south side, where a player standing outside
turns it. The front (west, -x, the way the body faces)
is open from the ground to the front beam, so the eidolon walks out of it when it wakes.

The chain hangs the gantry's spine (vanilla's eidolon's mast, cut off the body by the eidolon's
generator into ../spine.json) by a ring over its top peg (`spine-hook1`), there from the first stage
and after the eidolon has woken and stepped off it. The body is clamped to it, in the eidolon's `hung`
pose, baked: every element of the eidolon's shape (eidolon.json, read from the repository) and of the
spine, hung back on the chest block, is posed by `Eidolon/tools/kin.py`, the game's pose maths, at
`hung`'s one frame and written as a plain static element, the body's renamed `b_<stage>_<name>` by its
build stage (`config/eidolon-stages.json`), so the rig's `requires` shows the body stage by stage, and
the spine's `sp_<name>`. Everything else was made for the Seraph Horizons mod; the body's and the
spine's elements are Anego Studios' model (../../CREDITS.md).

It writes, deterministically,

    eidolongantry.json         the gantry, the winch and the body in every stage  (assets/.../shapes/block/)
    eidolongantry_frame.json   the static frame only (block and item)            (assets/.../shapes/block/)
    eidolongantry-rig.json     cells, anchors and the part rig                   (assets/.../config/)

or, with `--out DIR`, all three into DIR. It validates its own output (validate_eidolongantry.py)
and exits non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from
the machine box's north-west-bottom corner; the controller cell, the foot of the front right
(north-west) post, is that corner's cell, so the shipped files are this frame divided by 16. The
body faces west (-x); its right side is north (-z).

The rig's inputs, as the gantry uses them:

    depth  the winch let down, 0..1: the drum pays out `drop()` voxels of chain and the body comes
           down until its lowest toe is on the floor
    theta  the crank's clock, read by a ratio-0 rotate on the crank: it moves nothing (the viewer's
           Play runs the let-down by it; the let-down turns the crank RATIO times the drum)
"""

from __future__ import annotations

import argparse
import functools
import json
import math
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[1] / "Machines" / "tools"))
sys.path.insert(0, str(HERE.parents[1] / "Eidolon" / "tools"))
sys.path.insert(0, str(HERE))

import kin  # noqa: E402
from machinegen.checks import cell_boxes, cells_touched  # noqa: E402
from machinegen.checks import fix_coplanar as fix_coplanar_posed  # noqa: E402
from machinegen.geometry import IDENT, El, flatten, rotate  # noqa: E402
from machinegen.output import rig_dumps, shape_dumps  # noqa: E402
from machinegen.output import shape_json as machine_shape_json  # noqa: E402
from machinegen.rigmath import part_matrix, part_of, posed, validate_driver  # noqa: E402

ROOT = HERE.parents[3]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
EIDOLON = MOD / "assets" / "seraphhorizons" / "shapes" / "entity" / "eidolon" / "eidolon.json"
STAGES = RIG_DIR / "eidolon-stages.json"
SPINE = HERE.parent / "spine.json"            # vanilla's spine, cut off the eidolon by its generator: the gantry's
SCRIPT = "mods-src/seraphhorizons/EidolonGantry/tools/make_shape.py"

B = 16.0
TEX = 16                                     # shape texture units: one per voxel, the eidolon's own

# ---------------------------------------------------------------- the box, the cells
CELLS_X, CELLS_Y, CELLS_Z = 6, 6, 5          # six deep (x), six high (the top row half used), five wide (z)
ORIGIN_CELL = (0, 0, 0)                      # the controller: the front right (north-west) post's foot

GANTRY_TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "iron": "game:block/metal/plate/iron",
    "chain": "game:block/metal/armor-generic/chain-iron",
}

# ---------------------------------------------------------------- the body's place
BODY_AT = (30.0, 0.0, 40.0)                  # the entity's position (model (8, 0, 8)), where it stands once awake: the
#                                              hung body is the eidolon's HUNG_BACK (10 voxels) behind it, clamped to
#                                              the spine, round the middle of cell (2, 0, 2)
OFF = (BODY_AT[0] - 8.0, BODY_AT[1], BODY_AT[2] - 8.0)   # model voxels to build voxels
HUNG = "hung"                                # the animation the body is baked in (its one frame)

# ---------------------------------------------------------------- the frame (oak, 6 x 6 timbers)
T = 6.0                                      # a timber's section
X_FRONT, X_BACK = (0.0, T), (CELLS_X * B - T, CELLS_X * B)            # front (west) and back post lines
Z_RIGHT, Z_LEFT = (0.0, T), (CELLS_Z * B - T, CELLS_Z * B)            # right (north) and left (south) post lines
HEAD = (82.0, 88.0)                          # the head beams' underside and top; the posts stop under them
SILL_H = 5.0                                 # the sills (sides and back; the front is open)
RAIL = (40.0, 45.0)                          # the side rails: a player walks under them (a player is 29.6 tall)
RAIL_INSET = 0.5                             # the rails stand back from the posts' faces
KNEE = 4.0                                   # knee braces' section
KNEE_RUN = 20.0                              # how far a knee runs along its beam, and down its post
FRONT_KNEE_RUN = 16.0                        # the front knees, shorter: they stay over the exit's top corners
KNEE_EMBED = 1.5                             # how far a knee's ends run into the post and the beam
PEG = (1.2, 0.4)                             # an iron peg head's side and how far it stands proud
STRAP = (1.0, 0.3)                           # an iron plate's or bracket's width and thickness

# ---------------------------------------------------------------- the winch (at the back)
# A geared crab winch: the body is heavy (about 2,500 kg), so the crank turns a pinion that drives the drum
# through two spur stages, 10 to 50 teeth each, 25 to 1 in all. Three iron shafts run across the back in iron
# bearing plates on two tall oak cheeks bolted to the back posts: the crank shaft at the bottom (only on the
# left, where it runs on out through the pillow block to the crank), the layshaft above it and the drum's
# axle at the top. Both stages sit at the left end, between the drum and the left cheek: stage 1 (the crank's
# pinion and the layshaft's wheel) against the cheek, stage 2 (the layshaft's pinion and the drum's wheel)
# against the drum. A pawl on an iron bracket bolted to the back left post's outer face drops onto a ratchet
# on the crank shaft, outside the frame beside the crank, so the load cannot run back.
MODULE = 0.4                                 # the gears' module (voxels): pitch radius = MODULE * teeth / 2
PINION_TEETH, WHEEL_TEETH = 10, 50           # both stages
GEAR_STAGES = 2
RATIO = (WHEEL_TEETH / PINION_TEETH) ** GEAR_STAGES         # crank turns per drum turn: 25
R_PINION = MODULE * PINION_TEETH / 2         # 2
R_WHEEL = MODULE * WHEEL_TEETH / 2           # 10
MESH = R_PINION + R_WHEEL                    # a stage's centre distance
CRANK_AXIS = (88.0, 24.0)                    # the crank shaft (x, y), along z: the middle of the crank's cell
LAY = (85.0, CRANK_AXIS[1] + math.sqrt(MESH ** 2 - (CRANK_AXIS[0] - 85.0) ** 2))   # the layshaft, up and a little forward
DRUM = (LAY[0], LAY[1] + MESH)               # the drum's axle, straight above the layshaft
DRUM_R = 4.0                                 # its apothem (an octagon)
DRUM_Z = (16.0, 64.0)                        # the barrel
STAGE2_Z = (64.4, 66.2)                      # the drum's wheel and the layshaft's pinion
STAGE1_Z = (66.8, 68.6)                      # the layshaft's wheel and the crank's pinion
TOOTH_W = 0.5                                # a box tooth's width: under half the pitch at the meshing wheel's root, so
#                                              a tooth stands clear in the gap it turns into
PINION_PROUD = 0.1                           # a pinion is a little wider than its wheel, each side
CHEEK_W = 4.0                                # the cheeks' thickness (z), against the posts' inner faces
CHEEK_X = (80.0, CELLS_X * B)
CHEEK_Y = (CRANK_AXIS[1] - 6.0, DRUM[1] + 5.0)
BEARING = 2.5                                # a bearing plate's half side
AXLE_R = 1.0
# The crank is outside the frame, on the left (south) side: the crank shaft runs on past the left cheek, beside
# the back left post's front face in an iron pillow block bolted to it, and out past the post's outer face
# (z 80) into the crank's own cell (CRANK_CELL), where a player standing south of the gantry turns it.
AXLE_END = 85.0                              # the crank shaft's outer end (z)
PILLOW = (2.5, (75.0, 79.0))                 # the pillow block: its half-height and -depth (x, y) round the shaft, z
CRANK_Z = (82.0, 83.0)                       # the crank's web, outside the frame
CRANK_R = 6.5                                # the handle's radius about the shaft
HANDLE = (83.0, 88.0)                        # the handle runs this far along z, outwards, to the player's hand
HANDLE_W = 1.5                               # its section
CRANK_CELL = (5, 1, 5)                       # the crank's cell, south of the back left post, a block up
COIL_R = 4.6                                 # the chain wound on the drum, at its middle
COIL_W = 2.0
# The ratchet and its pawl, between the post's outer face and the crank's web. Letting down turns the crank
# shaft positive (about +z), which the pawl blocks: its nose sits in a gap, against a tooth's radial face. The
# pawl hangs from its pin on the bracket and falls onto the ratchet by its own weight; it is thrown off over
# the first PAWL_LIFT_TO of the let-down (the viewer shows it off while the winch is let down, on at hung).
RATCHET_TEETH = 8
RATCHET_R = (2.8, 4.0)                       # root and tip radius
RATCHET_TOOTH_W = 0.9                        # a tooth's width across, behind its radial face
RATCHET_Z = (80.65, 81.55)
PAWL_AT = (3.4, 105.0)                       # the pawl's nose: its middle's radius and angle (degrees from +x) about the shaft
PAWL_TILT = 25.0                             # the pawl leans this far outwards from the ratchet's tangent at its nose
PAWL_LEN, PAWL_W = 3.0, 0.8                  # nose to pin, and across
PAWL_GAP = 0.15                              # the nose's inner corner stands this far ahead of the tooth's face, at rest
PAWL_Z = (80.7, 81.5)
PAWL_LIFT = 0.5                              # radians the pawl is thrown off
PAWL_LIFT_TO = 0.003                         # over this much of the let-down
BRACKET_Z = (80.0, 80.5)                     # the pawl's bracket, on the post's outer face

# ---------------------------------------------------------------- the sheave, the chains, the ring
RING_X = 0.5                                 # the ring's bar thickness (x and the bars' depth)
RING_IN_Z = 2.1                              # its inside width (z): the peg (1.6 wide) passes through
RING_TOP_GAP = 1.55                          # its top bar stands this far above the peg's top
SHEAVE_R = 4.0                               # the sheave's hub apothem: the chain's radius over it
FLANGE_R = 5.5
SHEAVE_Y = 76.0                              # its axis: the flanges clear the hoist beam (82) by half a voxel
FLANGE_Z = ((37.0, 38.0), (42.0, 43.0))
HUB_Z = (38.0, 42.0)
HANGER_Z = ((35.5, 36.5), (43.5, 44.5))      # the iron hangers either side, from the hoist beam
CHAIN_W = 1.0                                # a chain's section
EYE = (1.0, 1.0)                             # the swivel eye at the fall's foot: height, side


# ---------------------------------------------------------------- box helpers
def skin(el, tex):
    """Every face takes `tex`, its UVs a region of the texture in proportion to the face's size."""
    axes = {"north": (0, 1), "south": (0, 1), "east": (2, 1), "west": (2, 1), "up": (0, 2), "down": (0, 2)}
    el.faces = {}
    for d, (u, v) in axes.items():
        w = min(abs(el.size[u]) * TEX / 16, TEX)
        h = min(abs(el.size[v]) * TEX / 16, TEX)
        el.faces[d] = {"texture": "#" + tex, "uv": [0.0, 0.0, w, h]}
    return el


def box(lo, hi, name, part, tex):
    c = [(lo[k] + hi[k]) / 2 for k in range(3)]
    return skin(El(name, [hi[k] - lo[k] for k in range(3)], c, [r[:] for r in IDENT], {}, part), tex)


def timber(lo, hi, name, part="frame", tex="oak", seg=16.0):
    """A long box split into block-long pieces along its longest axis, so the texture is not stretched."""
    axis = max(range(3), key=lambda a: hi[a] - lo[a])
    n = max(1, math.ceil((hi[axis] - lo[axis]) / seg - 1e-9))
    out = []
    for i in range(n):
        l2, h2 = list(lo), list(hi)
        l2[axis] = lo[axis] + (hi[axis] - lo[axis]) * i / n
        h2[axis] = lo[axis] + (hi[axis] - lo[axis]) * (i + 1) / n
        out.append(box(l2, h2, f"{name}{i + 1}" if n > 1 else name, part, tex))
    return out


def rod(a, b, w, d, name, part, tex):
    """A bar of section w x d from point a to point b, any direction: its local y along a->b."""
    u = [b[k] - a[k] for k in range(3)]
    length = math.sqrt(sum(x * x for x in u))
    u = [x / length for x in u]
    ref = [0.0, 0.0, 1.0] if abs(u[2]) < 0.9 else [1.0, 0.0, 0.0]
    xa = [u[1] * ref[2] - u[2] * ref[1], u[2] * ref[0] - u[0] * ref[2], u[0] * ref[1] - u[1] * ref[0]]
    n = math.sqrt(sum(x * x for x in xa))
    xa = [x / n for x in xa]
    za = [xa[1] * u[2] - xa[2] * u[1], xa[2] * u[0] - xa[0] * u[2], xa[0] * u[1] - xa[1] * u[0]]
    r = [[xa[i], u[i], za[i]] for i in range(3)]
    mid = [(a[k] + b[k]) / 2 for k in range(3)]
    return skin(El(name, [w, length, d], mid, r, {}, part), tex)


def octagon_z(z0, z1, cx, cy, apothem, name, part, tex):
    """A plain octagonal disc or drum along z: four strips turned 0, 45, 90 and 135 degrees about z."""
    half = apothem * math.tan(math.pi / 8)
    out = []
    for i in range(4):
        el = box([cx - apothem, cy - half, z0], [cx + apothem, cy + half, z1], f"{name}_{i + 1}", part, tex)
        if i:
            rotate([el], "z", 45.0 * i, (cx, cy, 0.0))
        out.append(el)
    return out


# ---------------------------------------------------------------- the body, baked
@functools.cache
def load_body():
    """The eidolon's shape and its stages, as the repository has them now (shared: never change them)."""
    return json.loads(EIDOLON.read_text()), json.loads(STAGES.read_text())


@functools.cache
def load_spine():
    return json.loads(SPINE.read_text())


def subtree(e):
    return [e["name"]] + [n for c in e.get("children", []) for n in subtree(c)]


@functools.cache
def spine_names():
    return frozenset(n for e in load_spine()["elements"] for n in subtree(e))


def with_spine(shape):
    """The eidolon's shape with the gantry's spine hung back where vanilla has it (a child of the chest
    block): how the two are posed together, the body clamped to the spine."""
    out = json.loads(json.dumps(shape))
    spine = load_spine()
    parent = kin.Rig(out).elements[spine["parent"]]
    parent.setdefault("children", []).extend(json.loads(json.dumps(spine["elements"])))
    return out


def baked_name(n, st):
    """A baked element's name: sp_<name> for the spine (the gantry's), b_<stage>_<name> for the body."""
    return f"sp_{n}" if n in spine_names() else f"b_{st[n]}_{n}"


def source_name(name):
    return name[3:] if name.startswith("sp_") else name.split("_", 2)[2]


def stage_of(stages):
    return {n: s["code"] for s in stages["stages"] for n in s["elements"]}


def body_codes(stages):
    """The stages that add elements, in build order (the first, the gantry itself, adds none)."""
    return [s["code"] for s in stages["stages"] if s["elements"]]


def drawn(e):
    return any(f.get("enabled", True) for f in e.get("faces", {}).values())


_HUNG = {}


def hung_matrices(shape):
    """kin's Rig of the shape with the spine on it and every element's model matrix (blocks) in the hung
    pose: the game's pose at its one frame (it does not move the spine, which is clamped to the chest)."""
    if id(shape) not in _HUNG:
        rig = kin.Rig(with_spine(shape))
        anim = next(a for a in shape["animations"] if a["code"] == HUNG)
        _HUNG[id(shape)] = (rig, rig.all_matrices(kin.sample(anim, 0)), shape)
    return _HUNG[id(shape)][:2]


def bake_body(shape, stages):
    """The body's elements and the spine's in the hung pose as static boxes (build voxels), named
    b_<stage>_<name> and sp_<name>, in the shape's order. Elements with no drawn face (vanilla's `origin`, the anchors) draw nothing and
    are left out. Disabled faces are dropped and so is wind data (a block's wind is its own); glow is
    kept, returned as {(name, face): glow} for `shape_json` to write back."""
    rig, mats = hung_matrices(shape)
    st = stage_of(stages)
    out, glow = [], {}
    for n in rig.order:
        e = rig.elements[n]
        if not drawn(e):
            continue
        m = mats[n]
        size = [e["to"][i] - e["from"][i] for i in range(3)]
        c = kin.apply(m, [s / 2 for s in size])
        r = [list(row) for row in kin.rotation_of(m)]
        faces = {}
        for d, f in e["faces"].items():
            if not f.get("enabled", True):
                continue
            face = {"texture": f["texture"], "uv": list(f["uv"])}
            if f.get("rotation"):
                face["rotation"] = f["rotation"]
            faces[d] = face
            if f.get("glow"):
                glow[(baked_name(n, st), d)] = f["glow"]
        part = "spine" if n in spine_names() else st[n]
        out.append(El(baked_name(n, st), size, [c[k] + OFF[k] for k in range(3)], r, faces, part))
    return out, glow


def body_corners(rig, mats, name):
    """An element's eight corners in the hung pose (build voxels), from kin: what `bake_body` must match."""
    return [[p[k] + OFF[k] for k in range(3)] for p in rig.corners(name, mats[name])]


def hang_point(shape):
    """Where the ring bears on the spine's top peg (`spine-hook1`), build voxels: on the peg's top face,
    1.5 ring bars in from its tip, across its middle. Also the top face's slope (dy/dx) and the peg's size."""
    rig, mats = hung_matrices(shape)
    e = rig.elements["spine-hook1"]
    size = [e["to"][i] - e["from"][i] for i in range(3)]
    m = mats["spine-hook1"]
    p0 = kin.apply(m, (0.0, size[1], size[2] / 2))
    p1 = kin.apply(m, (size[0], size[1], size[2] / 2))
    t = 1.0 - 1.5 * RING_X / size[0]
    p = [p0[k] + (p1[k] - p0[k]) * t + OFF[k] for k in range(3)]
    return p, (p1[1] - p0[1]) / (p1[0] - p0[0]), size


def lowest(els):
    return min(min(c[1] for c in el.corners()) for el in els)


# ---------------------------------------------------------------- the frame
NAMES = {X_FRONT: "f", X_BACK: "b", Z_RIGHT: "r", Z_LEFT: "l"}


def build_frame():
    out = []
    xs, zs = (X_FRONT, X_BACK), (Z_RIGHT, Z_LEFT)
    for x in xs:
        for z in zs:
            out += timber([x[0], 0.0, z[0]], [x[1], HEAD[0], z[1]], f"fr_post_{NAMES[x]}{NAMES[z]}")
    # the head: side beams over the posts, the front and back beams and the hoist beam between them
    for z in zs:
        out += timber([0.0, HEAD[0], z[0]], [CELLS_X * B, HEAD[1], z[1]], f"fr_head_{NAMES[z]}")
    for x in xs:
        out += timber([x[0], HEAD[0], T], [x[1], HEAD[1], CELLS_Z * B - T], f"fr_beam_{NAMES[x]}")
    hx = sheave_x()
    out += timber([hx - T / 2, HEAD[0], T], [hx + T / 2, HEAD[1], CELLS_Z * B - T], "fr_hoist")
    # the sills (no front sill: the exit) and the side rails
    for z in zs:
        out += timber([T, 0.0, z[0]], [CELLS_X * B - T, SILL_H, z[1]], f"fr_sill_{NAMES[z]}")
        out += timber([T, RAIL[0], z[0] + RAIL_INSET], [CELLS_X * B - T, RAIL[1], z[1] - RAIL_INSET], f"fr_rail_{NAMES[z]}")
    out += timber([X_BACK[0], 0.0, T], [X_BACK[1], SILL_H, CELLS_Z * B - T], "fr_sill_b")
    out += build_knees()
    out += build_irons()
    return out


def knee_ends():
    """Every knee brace as (name, a, b, plane axis, span across): from a post up to a beam, its ends
    KNEE_EMBED inside both. Side knees are in the x-y plane (a, b as (x, y)); front and back knees in
    the z-y plane ((y, z))."""
    out = []
    e = KNEE_EMBED
    for zn, z in (("r", Z_RIGHT), ("l", Z_LEFT)):
        out.append((f"fr_knee_{zn}f", (X_FRONT[1] - e, HEAD[0] - KNEE_RUN), (X_FRONT[1] + KNEE_RUN, HEAD[0] + e), "z", z))
        out.append((f"fr_knee_{zn}b", (X_BACK[0] + e, HEAD[0] - KNEE_RUN), (X_BACK[0] - KNEE_RUN, HEAD[0] + e), "z", z))
    for xn, x in (("f", X_FRONT), ("b", X_BACK)):
        run = FRONT_KNEE_RUN if xn == "f" else KNEE_RUN
        out.append((f"fr_knee_{xn}r", (HEAD[0] - run, Z_RIGHT[1] - e), (HEAD[0] + e, Z_RIGHT[1] + run), "x", x))
        out.append((f"fr_knee_{xn}l", (HEAD[0] - run, Z_LEFT[0] + e), (HEAD[0] + e, Z_LEFT[0] - run), "x", x))
    return out


def build_knees():
    out = []
    for name, a, b, axis, span in knee_ends():
        mid = (span[0] + span[1]) / 2
        if axis == "z":
            p, q = (a[0], a[1], mid), (b[0], b[1], mid)
        else:
            p, q = (mid, a[0], a[1]), (mid, b[0], b[1])
        out.append(rod(p, q, KNEE, KNEE, name, "frame", "oak"))
    return out


def build_irons():
    """Iron at the joints, all inside the machine box: a fish plate over each post's top joint on its
    inner side (up the post and onto the head beam), an angle bracket under each head beam's end on the
    post's inner end face, and trenail heads (pegs) on the head beams' tops over the posts and the knees
    and on the rails' outer faces at their ends."""
    out = []
    w, t = STRAP
    for x in (X_FRONT, X_BACK):
        for z in (Z_RIGHT, Z_LEFT):
            n = f"{NAMES[x]}{NAMES[z]}"
            zi = (z[1], z[1] + t) if z is Z_RIGHT else (z[0] - t, z[0])
            xm = (x[0] + x[1]) / 2
            out.append(box([xm - w / 2, HEAD[0] - 8.0, zi[0]], [xm + w / 2, HEAD[1] - 1.0, zi[1]], f"fr_iron_plate_{n}", "frame", "iron"))
            xi = (x[1], x[1] + t) if x is X_FRONT else (x[0] - t, x[0])
            zm = (z[0] + z[1]) / 2
            out.append(box([xi[0], HEAD[0] - 8.0, zm - w / 2], [xi[1], HEAD[0] - t, zm + w / 2], f"fr_iron_bracket_{n}a", "frame", "iron"))
            xu = (x[1], x[1] + 8.0) if x is X_FRONT else (x[0] - 8.0, x[0])
            out.append(box([xu[0], HEAD[0] - t, zm - w / 2], [xu[1], HEAD[0], zm + w / 2], f"fr_iron_bracket_{n}b", "frame", "iron"))
    s, proud = PEG
    for zn, z in (("r", Z_RIGHT), ("l", Z_LEFT)):
        zm = (z[0] + z[1]) / 2
        tops = [("post_f", (X_FRONT[0] + X_FRONT[1]) / 2), ("post_b", (X_BACK[0] + X_BACK[1]) / 2),
                ("knee_f", X_FRONT[1] + KNEE_RUN - 2.0), ("knee_b", X_BACK[0] - KNEE_RUN + 2.0)]
        for tn, xp in tops:
            out.append(box([xp - s / 2, HEAD[1], zm - s / 2], [xp + s / 2, HEAD[1] + proud, zm + s / 2], f"fr_iron_peg_{tn}{zn}", "frame", "iron"))
        zo = (z[0] + RAIL_INSET - proud, z[0] + RAIL_INSET) if zn == "r" else (z[1] - RAIL_INSET, z[1] - RAIL_INSET + proud)
        yc = (RAIL[0] + RAIL[1]) / 2
        for xn, xp in (("f", X_FRONT[1] + 2.0), ("b", X_BACK[0] - 2.0)):
            out.append(box([xp - s / 2, yc - s / 2, zo[0]], [xp + s / 2, yc + s / 2, zo[1]], f"fr_iron_peg_rail_{zn}{xn}", "frame", "iron"))
    return out


# ---------------------------------------------------------------- the winch
def cheek_z():
    return (Z_RIGHT[1], Z_RIGHT[1] + CHEEK_W), (Z_LEFT[0] - CHEEK_W, Z_LEFT[0])


def radial_z(c, z0, z1, r0, r1, y0, y1, ang, name, part, tex):
    """A box from radius r0 to r1 about the z axis through c, y0..y1 across (tangentially), turned `ang`
    radians from +x."""
    el = box([c[0] + r0, c[1] + y0, z0], [c[0] + r1, c[1] + y1, z1], name, part, tex)
    if abs(ang) > 1e-12:
        rotate([el], "z", math.degrees(ang), (c[0], c[1], 0.0))
    return el


def stage_phase(a, b):
    """The angle (radians) of the line of centres from a to b: a's pinion has a tooth on it at rest and b's
    wheel a gap, so the teeth sit meshed, not in each other."""
    return math.atan2(b[1] - a[1], b[0] - a[0])


def spur_z(c, z0, z1, pitch_r, teeth, name, part, tex, phase, spokes=0):
    """An iron spur gear about z: `teeth` box teeth of MODULE on its root circle, one at `phase`. A pinion is
    solid (an octagon to the root circle); a wheel (`spokes`) a rim, spokes and a hub."""
    root, tip = pitch_r - 1.25 * MODULE, pitch_r + MODULE
    w = TOOTH_W
    if spokes:
        rim_in, n = root - 1.3, 24
        half = root * math.tan(math.pi / n)
        out = [radial_z(c, z0 + (0.02 if i % 2 else 0.0), z1 - (0.02 if i % 2 else 0.0), rim_in, root - 0.1, -half, half,
                        phase + 2 * math.pi * (i + 0.5) / n, f"{name}_rim{i + 1}", part, tex) for i in range(n)]
        sw = 1.2
        out += [radial_z(c, z0 + 0.25, z1 - 0.25, 1.8, rim_in + 0.1, -sw / 2, sw / 2, phase + 2 * math.pi * (i + 0.5) / spokes,
                         f"{name}_spoke{i + 1}", part, tex) for i in range(spokes)]
        out += octagon_z(z0 - 0.15, z1 + 0.15, c[0], c[1], 2.2, f"{name}_hub", part, tex)
    else:
        out = octagon_z(z0, z1, c[0], c[1], root - 0.15, f"{name}_body", part, tex)   # its corners clear the wheel's tips
    for i in range(teeth):
        out.append(radial_z(c, z0 + 0.03, z1 - 0.03, root - 0.2, tip, -w / 2, w / 2, phase + 2 * math.pi * i / teeth,
                            f"{name}_tooth{i + 1}", part, tex))
    return out


def pawl_geometry():
    """The pawl at rest: its nose's middle, the unit vector along it from the nose to the pin, the pin (x, y),
    and the angle (radians) of the radial face of the ratchet tooth the nose bears against."""
    cx, cy = CRANK_AXIS
    r, a = PAWL_AT[0], math.radians(PAWL_AT[1])
    nose = (cx + r * math.cos(a), cy + r * math.sin(a))
    t = (-math.sin(a), math.cos(a))           # the way a tooth moves as the winch lets down (+z)
    n = (math.cos(a), math.sin(a))
    k = math.radians(PAWL_TILT)
    d = (math.cos(k) * t[0] + math.sin(k) * n[0], math.cos(k) * t[1] + math.sin(k) * n[1])
    pin = (nose[0] + PAWL_LEN * d[0], nose[1] + PAWL_LEN * d[1])
    perp = (-d[1], d[0])
    corners = [(nose[0] + s * PAWL_W / 2 * perp[0], nose[1] + s * PAWL_W / 2 * perp[1]) for s in (-1, 1)]
    inner = min(corners, key=lambda q: math.hypot(q[0] - cx, q[1] - cy))
    ri = math.hypot(inner[0] - cx, inner[1] - cy)
    face = math.atan2(inner[1] - cy, inner[0] - cx) - PAWL_GAP / ri
    # the nose's other corner may be further back: the face behind the further-back one
    for q in corners:
        rq = math.hypot(q[0] - cx, q[1] - cy)
        if rq < RATCHET_R[1]:
            face = min(face, math.atan2(q[1] - cy, q[0] - cx) - PAWL_GAP / rq)
    return nose, d, pin, face


def build_ratchet():
    """The ratchet on the crank shaft: a hub and RATCHET_TEETH teeth, each a box behind its radial face (the
    face the pawl stops), one tooth's face just behind the pawl's nose."""
    c = CRANK_AXIS
    z0, z1 = RATCHET_Z
    _, _, _, face = pawl_geometry()
    out = octagon_z(z0, z1, c[0], c[1], RATCHET_R[0] + 0.05, "ck_ratchet_body", "crank", "iron")
    for i in range(RATCHET_TEETH):
        out.append(radial_z(c, z0 + 0.03, z1 - 0.03, RATCHET_R[0] - 0.2, RATCHET_R[1], -RATCHET_TOOTH_W, 0.0,
                            face + 2 * math.pi * i / RATCHET_TEETH, f"ck_ratchet_tooth{i + 1}", "crank", "iron"))
    return out


def build_pawl():
    """The pawl (its own part, thrown off as the winch lets down), its pin and the bracket that carries it,
    bolted to the back left post's outer face (frame)."""
    nose, d, pin, _ = pawl_geometry()
    mid = ((nose[0] + pin[0]) / 2, (nose[1] + pin[1]) / 2, (PAWL_Z[0] + PAWL_Z[1]) / 2)
    el = box([mid[0] - PAWL_LEN / 2, mid[1] - PAWL_W / 2, PAWL_Z[0]], [mid[0] + PAWL_LEN / 2 + 0.6, mid[1] + PAWL_W / 2, PAWL_Z[1]],
             "pw_pawl", "pawl", "iron")
    rotate([el], "z", math.degrees(math.atan2(d[1], d[0])), mid)
    out = [el]
    out += octagon_z(BRACKET_Z[0], PAWL_Z[1] + 0.3, pin[0], pin[1], 0.45, "fr_iron_pawlpin", "frame", "iron")
    out.append(box([pin[0] - 1.0, pin[1] - 0.9, BRACKET_Z[0]], [X_BACK[1] - 0.5, pin[1] + 1.1, BRACKET_Z[1]],
                   "fr_iron_pawlbracket", "frame", "iron"))
    return out


def build_winch():
    """The winch: two tall oak cheeks bolted to the back posts, three iron shafts in iron bearing plates (the
    crank shaft, the layshaft, the drum's axle), the two gear stages, the oak drum with iron hoops and the
    chain coiled on it, the pillow block, and outside the frame the ratchet, its pawl and an iron crank with
    an oak handle."""
    out = []
    cz = cheek_z()
    for zn, z in zip("rl", cz):
        out += timber([CHEEK_X[0], CHEEK_Y[0], z[0]], [CHEEK_X[1], CHEEK_Y[1], z[1]], f"fr_cheek_{zn}")
        zp = (z[1], z[1] + 0.6) if zn == "r" else (z[0] - 0.6, z[0])
        for sn, c in (("ck", CRANK_AXIS), ("ls", LAY), ("dr", DRUM)):
            if sn == "ck" and zn == "r":
                continue                     # the crank shaft is only on the left
            out.append(box([c[0] - BEARING, c[1] - BEARING, zp[0]], [c[0] + BEARING, c[1] + BEARING, zp[1]],
                           f"fr_bearing_{sn}{zn}", "frame", "iron"))
    # the pillow block the crank shaft runs in where it passes the back left post, bolted to the post's front face
    hp, (pz0, pz1) = PILLOW
    cx, cy = CRANK_AXIS
    out.append(box([cx - hp, cy - hp, pz0], [X_BACK[0], cy + hp, pz1], "fr_bearing_post", "frame", "iron"))
    p = PINION_PROUD
    # the crank: its shaft from the stage-1 pinion out to the crank, the ratchet, the web and the handle
    out += octagon_z(STAGE1_Z[0] - p, AXLE_END, cx, cy, AXLE_R, "ck_shaft", "crank", "iron")
    out += spur_z(CRANK_AXIS, STAGE1_Z[0] - p, STAGE1_Z[1] + p, R_PINION, PINION_TEETH, "ck_pinion", "crank", "iron",
                  stage_phase(CRANK_AXIS, LAY))
    out += build_ratchet()
    out.append(box([cx - 1.25, cy - 1.75, CRANK_Z[0]], [cx + 1.25, cy + CRANK_R + 1.0, CRANK_Z[1]], "ck_web", "crank", "iron"))
    hw = HANDLE_W / 2
    out.append(box([cx - hw, cy + CRANK_R - hw, HANDLE[0]], [cx + hw, cy + CRANK_R + hw, HANDLE[1]], "ck_handle", "crank", "oak"))
    # the layshaft: the stage-1 wheel and the stage-2 pinion
    shaft_z = (cz[0][0] + 1.0, cz[1][1] - 1.0)
    out += octagon_z(shaft_z[0], shaft_z[1], LAY[0], LAY[1], AXLE_R, "ls_shaft", "layshaft", "iron")
    out += spur_z(LAY, STAGE1_Z[0], STAGE1_Z[1], R_WHEEL, WHEEL_TEETH, "ls_wheel", "layshaft", "iron",
                  stage_phase(LAY, CRANK_AXIS) + math.pi / WHEEL_TEETH, spokes=6)
    out += spur_z(LAY, STAGE2_Z[0] - p, STAGE2_Z[1] + p, R_PINION, PINION_TEETH, "ls_pinion", "layshaft", "iron",
                  stage_phase(LAY, DRUM))
    # the drum, its axle and the stage-2 wheel
    out += octagon_z(shaft_z[0], shaft_z[1], DRUM[0], DRUM[1], AXLE_R, "dr_axle", "drum", "iron")
    out += spur_z(DRUM, STAGE2_Z[0], STAGE2_Z[1], R_WHEEL, WHEEL_TEETH, "dr_wheel", "drum", "iron",
                  stage_phase(DRUM, LAY) + math.pi / WHEEL_TEETH, spokes=6)
    out += octagon_z(DRUM_Z[0], DRUM_Z[1], DRUM[0], DRUM[1], DRUM_R, "dr_drum", "drum", "oak")
    for i, z0 in enumerate((DRUM_Z[0] + 1.0, DRUM_Z[1] - 2.0), 1):
        out += octagon_z(z0, z0 + 1.0, DRUM[0], DRUM[1], DRUM_R + 0.3, f"dr_hoop{i}", "drum", "iron")
    out += octagon_z(BODY_AT[2] - COIL_W / 2, BODY_AT[2] + COIL_W / 2, DRUM[0], DRUM[1], COIL_R, "dr_coil", "drum", "chain")
    out += build_pawl()
    return out


# ---------------------------------------------------------------- the sheave, the chains, the ring
def drop_x():
    """The fall's line (x): straight above the ring on the peg."""
    return hang_point(load_body()[0])[0][0]


def sheave_x():
    return drop_x() + SHEAVE_R


def lead_tangents():
    """The lead chain from the drum to the sheave: the common tangent over both (the chain leaves the
    drum's top going forward and up, and runs onto the sheave's top from behind). Returns the two
    tangent points (x, y): on the drum, on the sheave."""
    sx, sy = sheave_x(), SHEAVE_Y
    dx, dy = DRUM
    vx, vy = sx - dx, sy - dy
    d = math.hypot(vx, vy)
    a = math.atan2(vy, vx)
    b = math.acos((DRUM_R - SHEAVE_R) / d)    # the outer tangent: its normal n has n . v = rd - rs
    nx, ny = math.cos(a + b), math.sin(a + b)
    if ny < 0:                                 # the one above both circles
        nx, ny = math.cos(a - b), math.sin(a - b)
    return (dx + DRUM_R * nx, dy + DRUM_R * ny), (sx + SHEAVE_R * nx, sy + SHEAVE_R * ny)


def eye_top():
    """The swivel eye's top, where the fall ends: above the ring's top bar."""
    p, _, _ = hang_point(load_body()[0])
    return p[1] + RING_TOP_GAP + RING_X + EYE[0]


def build_sheave():
    out = []
    sx, sy = sheave_x(), SHEAVE_Y
    for i, z in enumerate(HANGER_Z, 1):
        out.append(box([sx - 2.0, sy - 2.0, z[0]], [sx + 2.0, HEAD[0], z[1]], f"fr_hanger{i}", "frame", "iron"))
    out += octagon_z(HANGER_Z[0][0] - 0.5, HANGER_Z[1][1] + 0.5, sx, sy, 0.8, "sv_pin", "sheave", "iron")
    out += octagon_z(HUB_Z[0], HUB_Z[1], sx, sy, SHEAVE_R, "sv_hub", "sheave", "oak")
    for i, z in enumerate(FLANGE_Z, 1):
        out += octagon_z(z[0], z[1], sx, sy, FLANGE_R, f"sv_flange{i}", "sheave", "oak")
    return out


def build_ring():
    """An iron ring in the y-z plane round the peg: its bottom bar under the peg bears the body, its top
    bar is the eye's."""
    p, slope, size = hang_point(load_body()[0])
    x, zc = p[0], BODY_AT[2]
    xs = (x - RING_X / 2, x + RING_X / 2)
    # the peg's underside across the ring's thickness, at its lowest: its top face less its depth, upright
    under = p[1] - abs(slope) * RING_X / 2 - size[1] * math.sqrt(1 + slope * slope)
    y0 = under - RING_X
    top = p[1] + RING_TOP_GAP
    zi = (zc - RING_IN_Z / 2, zc + RING_IN_Z / 2)
    return [
        box([xs[0], top, zi[0] - RING_X], [xs[1], top + RING_X, zi[1] + RING_X], "rg_top", "ring", "iron"),
        box([xs[0], y0, zi[0] - RING_X], [xs[1], y0 + RING_X, zi[1] + RING_X], "rg_bottom", "ring", "iron"),
        box([xs[0], y0 + RING_X, zi[0] - RING_X], [xs[1], top, zi[0]], "rg_side_r", "ring", "iron"),
        box([xs[0], y0 + RING_X, zi[1]], [xs[1], top, zi[1] + RING_X], "rg_side_l", "ring", "iron"),
    ]


def build_chains():
    """The lead chain (drum to sheave, fixed), the fall (sheave to the eye, stretched as the drum pays
    out), the swivel eye at its foot and the ring over the spine's peg."""
    zc = BODY_AT[2]
    (ax, ay), (bx, by) = lead_tangents()
    x, top = drop_x(), eye_top()
    out = [rod((ax, ay, zc), (bx, by, zc), CHAIN_W, CHAIN_W, "ld_chain", "lead", "chain"),
           box([x - CHAIN_W / 2, top, zc - CHAIN_W / 2], [x + CHAIN_W / 2, SHEAVE_Y, zc + CHAIN_W / 2], "fl_chain", "fall", "chain"),
           box([x - EYE[1] / 2, top - EYE[0], zc - EYE[1] / 2], [x + EYE[1] / 2, top, zc + EYE[1] / 2], "hk_eye", "hook", "iron")]
    return out + build_ring()


def build_gantry():
    return build_chains() + build_sheave() + build_winch() + build_frame()


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


@functools.cache
def drop():
    """How far the body comes down at depth 1 (voxels): until its lowest toe is on the floor."""
    shape, stages = load_body()
    return lowest([el for el in bake_body(shape, stages)[0] if el.part != "spine"])


def rig_parts(stages):
    d = drop()
    zc = BODY_AT[2]
    turn = d / DRUM_R                         # the drum's turn over the let-down (radians)
    parts = [
        # the gear train, by depth: the drum pays out the drop and the layshaft and the crank turn by the tooth
        # counts, each the other way to the gear it meshes with; the crank's clock (theta) rides on the crank
        # and moves nothing
        {"id": "crank", "match": ["ck_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "z", "pivot": pt(*CRANK_AXIS, zc), "ratio": 0.0},
                     {"type": "step", "motion": "rotate", "axis": "z", "pivot": pt(*CRANK_AXIS, zc),
                      "from": 0.0, "to": 1.0, "amount": r6(turn * RATIO)}]},
        {"id": "layshaft", "match": ["ls_*"], "requires": None,
         "drivers": [{"type": "step", "motion": "rotate", "axis": "z", "pivot": pt(*LAY, zc),
                      "from": 0.0, "to": 1.0, "amount": r6(-turn * WHEEL_TEETH / PINION_TEETH)}]},
        {"id": "drum", "match": ["dr_*"], "requires": None,
         "drivers": [{"type": "step", "motion": "rotate", "axis": "z", "pivot": pt(*DRUM, zc),
                      "from": 0.0, "to": 1.0, "amount": r6(turn)}]},
        # the pawl, thrown off the ratchet as the let-down starts, back on when the body is wound up to hung
        {"id": "pawl", "match": ["pw_*"], "requires": None,
         "drivers": [{"type": "step", "motion": "rotate", "axis": "z", "pivot": pt(*pawl_geometry()[2], zc),
                      "from": 0.0, "to": PAWL_LIFT_TO, "amount": PAWL_LIFT}]},
        {"id": "sheave", "match": ["sv_*"], "requires": None,
         "drivers": [{"type": "step", "motion": "rotate", "axis": "z", "pivot": pt(sheave_x(), SHEAVE_Y, zc),
                      "from": 0.0, "to": 1.0, "amount": r6(d / SHEAVE_R)}]},
        {"id": "lead", "match": ["ld_*"], "requires": None, "drivers": []},
        {"id": "fall", "match": ["fl_*"], "requires": None,
         "drivers": [{"type": "stretch", "axis": "y", "anchor": pt(drop_x(), SHEAVE_Y, zc),
                      "length": r6((SHEAVE_Y - eye_top()) / B), "travel": r6(d / B)}]},
        {"id": "hook", "match": ["hk_*"], "requires": None, "drivers": [{"type": "feed", "axis": "y", "travel": r6(-d / B)}]},
        # the ring over the spine's top peg, and the spine: the gantry's, there from the first stage on,
        # empty before the torso is clamped to it and again once the eidolon has woken and stepped off
        {"id": "ring", "match": ["rg_*"], "requires": None, "ride": "hook", "drivers": []},
        {"id": "spine", "match": ["sp_*"], "requires": None, "ride": "hook", "drivers": []},
    ]
    for code in body_codes(stages):
        parts.append({"id": code, "match": [f"b_{code}_*"], "requires": code, "ride": "hook", "drivers": []})
    parts.append({"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []})
    for p in parts:
        p.setdefault("ride", None)
        for drv in p["drivers"]:
            validate_driver(drv)
    return parts


def pm(parts, pid, depth, theta=0.0):
    return part_matrix(parts, pid, {"theta": theta, "depth": depth})


def footprint():
    """The machine box's cells and the crank's, outside it."""
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)] + [CRANK_CELL]


def regions():
    """Where the model may be (voxels, (lo, hi)): the machine box, and the crank's column, from the drum
    out through the crank's cell."""
    cx, cy, cz = CRANK_CELL
    return [((0.0, 0.0, 0.0), (CELLS_X * B, CELLS_Y * B, CELLS_Z * B)),
            ((cx * B, cy * B, 0.0), ((cx + 1) * B, (cy + 1) * B, (cz + 1) * B))]


CELL_PARTS = ("frame", "crank", "layshaft", "drum", "pawl", "sheave")   # what the cells' boxes are made of: the gantry; the body's
#                                              are gameplay's
#                                              (the spine's too: it hangs, and goes up and down with the body)


def shipped_cells(shape, parts):
    """The cells' boxes from the shipped shape as written, at rest; cells with none of the gantry in them
    are hollow (the walk-in space, the body's room and the exit), and so is the crank's, outside the frame."""
    by_cell = {}
    for w in flatten(shape["elements"], textures={}):
        pid = part_of(parts, w.name)
        if pid not in CELL_PARTS:
            continue
        el = posed(w, pm(parts, pid, 0.0))
        lo, hi = el.aabb()
        for c in cells_touched(lo, hi):
            by_cell.setdefault(c, []).append(el)
    out = []
    for c in footprint():
        # the crank's cell reserves the crank's room but has no boxes: nothing there to walk into or select
        boxes = cell_boxes(by_cell[c], c) if c in by_cell and c != CRANK_CELL else None
        out.append({"pos": list(c), "boxes": boxes} if boxes else {"pos": list(c), "hollow": True})
    return out


def fit_point():
    """Where the player fits parts: the middle of the body's chest (build voxels)."""
    rig, mats = hung_matrices(load_body()[0])
    c = rig.centre("chest-inside", mats["chest-inside"])
    return [c[k] + OFF[k] for k in range(3)]


def make_rig(parts):
    peg, _, _ = hang_point(load_body()[0])
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the foot of the front "
                    "right (north-west) post. The body faces west (-x), the exit; its right is north. Inputs: depth, the winch "
                    "let down 0..1 (the body comes down until its lowest toe is on the floor); theta, the crank's clock, moves "
                    "nothing. requires: one value per build stage (eidolon-stages.json) for the body's parts; the spine and the "
                    "ring over its top peg are the gantry's, always there (with no stage fitted: the gantry built, or the "
                    "eidolon woken and gone). body: the entity's position, where it stands once awake; hang: where the ring "
                    "bears on the peg; fit: the chest's middle; crankCell and crankFace: the crank, outside the frame, turned "
                    "from the south (its cell reserves the room and is hollow: no boxes). The cells' boxes are the gantry's; the body's are gameplay's. See EidolonGantry/README.md.",
        "cells": [],
        "body": {"pos": pt(*BODY_AT)},
        "hang": {"pos": pt(*peg)},
        "fit": {"pos": pt(*fit_point())},
        "exit": {"pos": pt(0.0, 0.0, BODY_AT[2])},
        "exitSide": "west",
        "crankCell": list(CRANK_CELL),
        "crankFace": "south",
        "winch": {"drop": r6(drop() / B), "drumRadius": r6(DRUM_R / B),
                  "gearing": {"stages": [[PINION_TEETH, WHEEL_TEETH]] * GEAR_STAGES, "ratio": RATIO},
                  "_comment": "drop: how far the body comes down at depth 1 (blocks), its hung toe's height above the floor; "
                              "drumRadius: the chain's radius on the drum (blocks); gearing: the crank's train to the drum, "
                              "[pinion, wheel] teeth per stage, crank turns per drum turn."},
        "parts": parts,
    }


# ---------------------------------------------------------------- shape files
def shape_json(els, glow, textures):
    shape = machine_shape_json(
        els, f"Generated by {SCRIPT}. The gantry, its winch and chains were made for the Seraph Horizons mod. The "
             "elements named b_<stage>_<name> are the eidolon's body and those named sp_<name> its spine, the gantry's "
             "(Anego Studios' model, Vintage Story's mobile eidolon; see CREDITS.md), posed in the eidolon's 'hung' "
             "animation and baked into static boxes, the body's renamed by build stage. "
             "Keep element names when editing: the rig finds its parts by them.",
        textures, tex_size=TEX)
    for e in shape["elements"]:
        for d, f in e["faces"].items():
            if (e["name"], d) in glow:
                f["glow"] = glow[(e["name"], d)]
    return shape


def textures_of(body_shape):
    out = dict(GANTRY_TEXTURES)
    for k, v in body_shape["textures"].items():
        if k in out and out[k] != v:
            raise SystemExit(f"texture code {k!r} is the gantry's and the body's, with different paths")
        out[k] = v
    return out


COPLANAR_POSES = (0.0, 0.5, 1.0)


def fix_coplanar(gantry, parts):
    def posed_fn(es, depth):
        return [posed(el, pm(parts, el.part, depth)) for el in es]
    return fix_coplanar_posed(gantry, posed_fn, COPLANAR_POSES)


def main():
    ap = argparse.ArgumentParser(description="Generate the eidolon gantry's shapes and rig.")
    ap.add_argument("--out", type=Path, help="write the three files into this directory instead of the mod's assets")
    args = ap.parse_args()
    import validate_eidolongantry
    body_shape, stages = load_body()
    body, glow = bake_body(body_shape, stages)
    parts = rig_parts(stages)
    gantry = build_gantry()
    before, hidden = fix_coplanar(gantry, parts)
    for pose, pairs in before.items():
        print(f"coplanar faces before the fix at depth {pose}: {len(pairs)} pairs")
    print(f"coplanar faces: {hidden} faces pressed against their own part removed")
    els = gantry + body
    rig = make_rig(parts)
    ok = validate_eidolongantry.validate(sys.modules[__name__], els, parts, rig, body_shape, stages)
    textures = textures_of(body_shape)
    shape = shape_json(els, glow, textures)
    frame_shape = shape_json([el for el in els if el.part == "frame"], {}, textures)
    rig["cells"] = shipped_cells(shape, parts)
    ok = validate_eidolongantry.validate_files(sys.modules[__name__], shape, frame_shape, rig, body_shape, stages) and ok
    if args.out:
        outs = (args.out / "eidolongantry.json", args.out / "eidolongantry_frame.json", args.out / "eidolongantry-rig.json")
    else:
        outs = (SHAPE_DIR / "eidolongantry.json", SHAPE_DIR / "eidolongantry_frame.json", RIG_DIR / "eidolongantry-rig.json")
    for path, text in zip(outs, (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(rig))):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
        json.loads(path.read_text())
        print(f"wrote {path} ({path.stat().st_size // 1024} KiB)")
    if not ok:
        sys.exit(1)
    print("all checks passed")


if __name__ == "__main__":
    main()
