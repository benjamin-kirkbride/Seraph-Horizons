#!/usr/bin/env python3
"""Generate the sluice's shapes and rig.

The sluice is the first concentrator of the ore line (#734, model #735): a riffled plank sluice of a
stamp mill, "10 to 15 feet of sluice boxes fitted with transverse riffles" [Louis 1902, p. 293], scaled
to three blocks. The pulp comes down a vanilla copper chute into a plank head box at the head, where a
ppex water pipe (drawn soldered, in copper or lead, as UnifiedPipes draws them) comes in under the box and
rises through its floor to a nozzle; it overflows a sill onto the sluice, a plank trough falling 1 in 12 on
two trestles, seven oak riffles across its floor, each faced with strap iron. The heavy grains settle
behind the riffles and are drawn off as the sluice runs, through a draw slot in the floor behind each
riffle, into the concentrate launder slung under the floor, which falls with the sluice to the concentrate
box under the tail; the box opens into the concentrate chute, a vanilla chute section flush with the tail
cell's west face. The rest runs off the tail into the tailings chute, flush with the tail's south face.
Nothing moves: there is no power, no axle and no driver. Everything is built here from plain boxes; no other
mod's model is used.

It writes, deterministically,

    sluice.json         the whole sluice, every fitted part and the concentrate          (assets/.../shapes/block/)
    sluice_frame.json   the frame only: the head box, the sluice box, the launder, the
                        concentrate box, the trestles                                     (assets/.../shapes/block/)
    sluice-rig.json     the cells, the anchors and the part rig                           (assets/.../config/)

or, with `--out DIR`, all three into DIR. It validates its own output (validate_sluice.py) and exits
non-zero if a check fails. There are no reference poses: no part has a driver, so every part's matrix is
the identity at any input, which the validator and the tests check instead.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the
machine box's north-west-bottom corner (the "build frame"). The controller is the head cell, [0,0,0], the
build frame's own corner, so the shipped files are the build frame divided by 16. The player stands at the
head (north) and the sluice runs away from them, south, three blocks.

The vanilla chute's profile (VANILLA_CHUTE, chute_profile, vanilla_profile, read_vanilla_chute) and the
helpers skin, box, sloped, slope_len, along, floor_y and under_slope are taken from the concentrator's v2
generator (branch 735-concentrator-model-v2), adapted.
"""

from __future__ import annotations

import argparse
import copy
import json
import math
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Machines" / "tools"))
sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Pipes" / "tools"))

from machinegen.checks import cell_boxes, cells_touched  # noqa: E402
from machinegen.checks import fix_coplanar as fix_coplanar_posed  # noqa: E402
from machinegen.geometry import IDENT, El, flatten, rotate, translate  # noqa: E402
from machinegen.output import rig_dumps, shape_dumps, shift_cell, shift_point, worst_shift_error  # noqa: E402
from machinegen.output import shape_json as machine_shape_json  # noqa: E402
from machinegen.rigmath import part_of, posed, validate_driver  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402

import solderedpipe  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
SCRIPT = "mods-src/seraphhorizons/Sluice/tools/make_shape.py"

B = 16.0

# ---------------------------------------------------------------- the box, the cells, the anchors
CELLS_X, CELLS_Y, CELLS_Z = 1, 1, 3          # one wide, one high, three long (z, away from the player)
ORIGIN_CELL = (0, 0, 0)                      # the controller: the head cell, nearest the player
CX = 8.0                                     # the machine's centre line (x): the sluice's, the infeed's, the tailings' and the pipe's
INFEED_CELL, INFEED_FACE = (0, 0, 0), "up"   # a vanilla chute from above meets the infeed section at the head cell's top
WATER_CELL, WATER_FACE = (0, 0, 0), "north"  # a ppex pipe meets the water inlet on the head's end face
TAILINGS_CELL, TAILINGS_FACE = (0, 0, 2), "south"   # the tailings chute leaves through the tail's end face
CONC_CELL, CONC_FACE = (0, 0, 2), "west"     # the concentrate chute leaves through the tail cell's west face

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",         # timber: the trestles, bearers and sleepers, the riffles' bars
    "planks": "game:block/wood/planks/oak1",       # boards: the head box, the sluice, the launder, the concentrate box
    "iron": "game:block/metal/plate/iron",         # the riffles' wearing straps
    "chute": "game:block/metal/sheet/copper1",     # the three chute sections: vanilla's chute, as placed in the pack
    "pipecopper": "game:block/metal/sheet-plain/copper4",   # the water inlet in copper (UnifiedPipes' copper pipe)
    "pipelead": "game:block/metal/sheet-plain/lead4",       # ... in lead (UnifiedPipes' lead pipe)
    "solder": solderedpipe.SOLDER_TEXTURE,         # (the pipes' joints: none inside the machine; kept for the copies)
    "concentrate": "game:block/stone/sand/basalt",  # the concentrate: a dark heavy sand (its own code, for a renderer)
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the vanilla chute
VANILLA_CHUTE = {                            # game 1.22.7, shapes/block/metal/chute/straight.json (the "we" shape):
    "walls": ((4.0, 12.0, 4.0, 5.0),         # its walls across the run, (a0, a1, b0, b1) cell-local over (y, z): two
              (4.0, 12.0, 11.0, 12.0),       # full-width walls, two between them; 1 thick, an 8 x 8 tube centred in
              (4.0, 5.0, 5.0, 11.0),         # the block (its block's 4..12), a 6 x 6 bore. Its "ud" variant is the
              (11.0, 12.0, 5.0, 11.0)),      # same turned upright (rotateZ 90), "ns" turned about y (rotateY 90)
    "texture": "game:block/metal/sheet/copper1",
    "shape": "shapes/block/metal/chute/straight.json",
}
CHUTE_BORE = (5.0, 11.0)                     # the bore, cell-local, across either axis
CHUTE_IN_Y0 = 14.5                           # the infeed section: from the head cell's top face down to here, into the head box
CHUTE_OUT_Z0 = 44.0                          # the tailings section: from here to the tail's end face (z 48)
CHUTE_CONC_X1 = 4.0                          # the concentrate section: from the tail cell's west face (x 0) to here, the box's mouth

# ---------------------------------------------------------------- the sluice
SL_Z = (13.0, CHUTE_OUT_Z0)                  # its head (against the head box) .. its tail (the tailings chute's mouth)
SL_HEAD_Y = 13.0                             # its floor's top at the head: the head box's sill, a voxel over the box's floor
SL_SLOPE = 1.0 / 12.0                        # it falls 1 in 12 (Louis: an inch to the foot), head to tail
SL_TAIL_Y = SL_HEAD_Y - (SL_Z[1] - SL_Z[0]) * SL_SLOPE   # its floor's top at the tail, 10.42: in the tailings chute's mouth,
                                             # under its bore's top (11), leaving room under the floor for the launder
SL_IN = (4.95, 11.05)                        # the channel: 6.1 wide, over the chute's 6 bore by a hair each side
SL_SIDE_T = 0.9                              # its sides (the boards' thickness): outside 4.05 .. 11.95, inside the chute's walls
SL_FLOOR_T = 1.0
SL_SIDE_H = 2.5                              # the sides above the floor's top
SL_SPLITS = (23.5, 34.0)                     # the sides are three boards' lengths each (none longer than the texture),
                                             # butted here, each joint over a trestle
THETA = math.atan(SL_SLOPE)                  # the slope's angle

# ---------------------------------------------------------------- the riffles (`riffles`) and the draw slots
RIFFLE_Z0 = 18.0                             # the first riffle's upstream face (on the floor's top line): 5 of plain floor at
RIFFLE_PITCH = 4.0                           # the head for the pulp to spread on; then one every 4
RIFFLE_N = 7
RIFFLE_T = 1.0                               # each an oak bar 1 along the flow ...
RIFFLE_H = 1.1                               # ... and 1.1 high, ...
STRAP_T = 0.2                                # ... faced on top with strap iron 0.2 thick (the wearing surface) ...
STRAP_LIP = 0.3                              # ... overhanging its downstream face (a Hungarian riffle's lip)
SLOT = 1.0                                   # the draw slot through the floor right behind each riffle (along the slope),
                                             # across the channel; behind riffles 1..6 (the 7th stands at the lip)

# ---------------------------------------------------------------- the concentrate launder, under the floor
LD_Z = (18.0, 41.0)                          # from just upstream of the first slot to its open end over the concentrate box
LD_SPLIT_Z = 29.5                            # two boards' lengths
LD_T = 0.8                                   # its boards
LD_DEPTH = 1.6                               # inside, under the sluice's floor
LD_OUT = (4.15, 11.85)                       # outside across: under the sluice's sides; inside, the channel (the slots drop straight in)
LD_H = (-SL_FLOOR_T - LD_DEPTH - LD_T, -SL_FLOOR_T)   # its height over the floor's top line: bottom .. its sides' top, on the floor

# ---------------------------------------------------------------- the concentrate box, under the tail, and its chute
CB_X = (4.0, 12.0)                           # outside; open on the west over the chute's bore (its inside is the bore's z)
CB_Z = (36.0, 44.0)                          # the tail cell's face centre z 40 +- 4: the chute's section
CB_Y0 = 4.0                                  # its floor's underside: the chute's bottom wall's; its floor's top the bore floor (5)
CB_T = 1.0
CB_TOP = 6.8                                 # its north and east walls' top, under the launder; the south wall rises to the
                                             # sluice's floor, closing the tailings chute's mouth under the lip
CB_SLEEPERS_Z = ((37.2, 38.6), (41.4, 42.8))  # two oak sleepers under it
CB_SLEEPER_X = (4.5, 11.5)

# ---------------------------------------------------------------- the concentrate (`concentrate`)
CONC_FULL = 1.0                              # a full pocket's depth over the floor: 0.1 under the straps' lips
CONC_IN = 0.05                               # how far a deposit runs into the riffle it lies against, the floor and the sides
# The bed each pocket holds while the sluice runs (pocket i lies behind riffle i, up to riffle i + 1), drawn off
# through its slot as fast as it settles: deeper at the head, where the heavy grains settle first.
BED = (0.75, 0.7, 0.65, 0.6, 0.55, 0.5)
CONC_LAYER = 0.25                            # the concentrate running down the launder's floor and lying in the box

# ---------------------------------------------------------------- the head box
HB_X = (3.0, 13.0)                           # outside; plank walls 1 thick round the infeed chute's 8 x 8
HB_Z = (3.0, 13.0)
HB_Y = (11.0, 15.5)                          # its floor's underside (on the water pipe and its bearers) and its walls' top
HB_T = 1.0
HB_FLOOR = 12.0                              # its floor's top, a voxel under the sill: the pool the ore falls into
HB_NOTCH = (SL_IN[0] - 0.05, SL_IN[1] + 0.05)   # the south wall's notch over the sill: the channel and a hair, so the
                                             # sides' ends, cut square to the slope, sink into the wall beside it unseen

# ---------------------------------------------------------------- the trestles
LEG = 1.6                                    # the legs, square
BEAM_H, BEAM_D = 1.4, 1.2                    # the cross beams and bearers: high, deep (narrower than the legs, mortised in)
HB_LEG_TOP = 14.5                            # the head box's four corner legs rise beside its walls to here
SL_TRESTLES_Z = SL_SPLITS                    # the sluice's two (centres): under the sides' joints
SL_LEG_DROP = 0.5                            # their legs stop this far under the sides' top

# ---------------------------------------------------------------- the water inlet (soldered ppex pipe)
PIPE_HALF = solderedpipe.HALF                # 3: ppex's pipe is 6 across
WATER_IN = (CX, 8.0)                         # (x, y): the inlet's axis, the middle of the water face
RISER_Z = 7.0                                # the pipe runs south under the head box to here and turns up through its floor
REDUCER_T = 0.3                              # the reducer on the riser's end, on the box's floor
NOZZLE = ((1.5, 0.3), (1.0, 0.3))            # the nozzle: two steps up from the reducer, (half across, height): its mouth
                                             # under the sill, in the pool, under the falling ore
PIPE_METALS = ("copper", "lead")             # one part each, the same elements; only the supplied metal's is drawn
PIPES = tuple(f"pipe{m}" for m in PIPE_METALS)


# ---------------------------------------------------------------- box helpers
FACE_AXES = {"north": (0, 1), "south": (0, 1), "east": (2, 1), "west": (2, 1), "up": (0, 2), "down": (0, 2)}


def skin(el, tex, along_z=False):
    """Every face takes `tex`, its UVs a region of the texture in proportion to the face's size. With
    `along_z`, the up and down faces are turned a quarter (rotation 90), so a plank texture's boards run
    along the element's local z, as a floor's boards run along the trough."""
    el.faces = {}
    for d, (u, v) in FACE_AXES.items():
        if along_z and d in ("up", "down"):
            u, v = v, u
        w = min(abs(el.size[u]) * TEX / 16, TEX)
        h = min(abs(el.size[v]) * TEX / 16, TEX)
        el.faces[d] = {"texture": "#" + tex, "uv": [0.0, 0.0, w, h]}
        if along_z and d in ("up", "down"):
            el.faces[d]["rotation"] = 90
    return el


def box(lo, hi, name, part, tex, along_z=False):
    c = [(lo[k] + hi[k]) / 2 for k in range(3)]
    return skin(El(name, [hi[k] - lo[k] for k in range(3)], c, [r[:] for r in IDENT], {}, part), tex, along_z)


def slope_len(a0, a1, y0, y1):
    return math.hypot(a1 - a0, y1 - y0)


def along(a0, a1, y0, y1, at):
    """The distance along a slope from its a0 end to the point over `at`."""
    return abs(at - a0) * slope_len(a0, a1, y0, y1) / abs(a1 - a0)


def floor_y(y0, y1, a0, a1, at):
    """A floor's top at `at` along its axis (its top runs straight from y0 at a0 to y1 at a1)."""
    return y0 + (y1 - y0) * (at - a0) / (a1 - a0)


def sloped(pieces, part):
    """Boxes laid on the sluice's slope, built level from its head (z SL_Z[0], height SL_HEAD_Y) and turned
    about the head's line, so the head stays where it is. Each piece is (s0, s1, h0, h1, x0, x1, name, tex):
    s along the slope from the head, h above the floor's top line (normal to the slope), x across."""
    a0 = SL_Z[0]
    out = []
    for s0, s1, h0, h1, x0, x1, name, tex in pieces:
        out.append(box([x0, SL_HEAD_Y + h0, a0 + s0], [x1, SL_HEAD_Y + h1, a0 + s1], name, part, tex,
                       along_z=(tex == "planks")))
    rotate(out, "x", math.degrees(THETA), (0.0, SL_HEAD_Y, a0))
    return out


def s_at(z):
    """The distance along the floor's top line from the head to the point over z."""
    return along(SL_Z[0], SL_Z[1], SL_HEAD_Y, SL_TAIL_Y, z)


def sl_floor(z):
    """The sluice floor's top over z."""
    return floor_y(SL_HEAD_Y, SL_TAIL_Y, SL_Z[0], SL_Z[1], z)


def slope_point(s, h, x=CX):
    """The build-frame point at s along the slope and h over the floor's top line."""
    return [x, SL_HEAD_Y - s * math.sin(THETA) + h * math.cos(THETA), SL_Z[0] + s * math.cos(THETA) + h * math.sin(THETA)]


def s_end_at(z, h):
    """The s at which a sloped piece's corner at height h (over the floor's top line) lies over z."""
    return (z - SL_Z[0] - h * math.sin(THETA)) / math.cos(THETA)


def under_slope(lo, hi, h):
    """The lowest point over lo..hi along z of the slope's line at height h (a sloped piece's underside), less
    a hundredth: where a beam or a wall under it stops."""
    return min(sl_floor(lo), sl_floor(hi)) + h / math.cos(THETA) - 0.01


# ---------------------------------------------------------------- the vanilla chute's profile
def chute_walls(axis, a0, a1, cell, name, part):
    """A vanilla straight chute section running along `axis` ("x" east-west, "y" upright, "z" north-south)
    from a0 to a1 in `cell`: VANILLA_CHUTE's walls, its section, wall thickness, place in the block and
    texture exactly, so a chute placed beyond meets it with no gap or step. Named for the side each wall
    faces."""
    o = [cell[k] * B for k in range(3)]
    out = []
    if axis == "x":
        # east-west ("we"): the game's shape as it is, over (y, z)
        for (y0, y1, z0, z1), side in zip(VANILLA_CHUTE["walls"], ("n", "s", "bottom", "top")):
            out.append(box([a0, o[1] + y0, o[2] + z0], [a1, o[1] + y1, o[2] + z1], f"{name}_{side}", part, "chute"))
    elif axis == "y":
        # upright ("ud"): the "we" shape turned about z, its y becoming x (mirrored, which the square
        # section does not mind) and its z staying z
        for (y0, y1, z0, z1), side in zip(VANILLA_CHUTE["walls"], ("n", "s", "e", "w")):
            out.append(box([o[0] + 16.0 - y1, a0, o[2] + z0], [o[0] + 16.0 - y0, a1, o[2] + z1], f"{name}_{side}", part, "chute"))
    else:
        # north-south ("ns"): the "we" shape turned about y, its z becoming x (mirrored) and its y staying y
        for (y0, y1, z0, z1), side in zip(VANILLA_CHUTE["walls"], ("e", "w", "bottom", "top")):
            out.append(box([o[0] + 16.0 - z1, o[1] + y0, a0], [o[0] + 16.0 - z0, o[1] + y1, a1], f"{name}_{side}", part, "chute"))
    return out


ACROSS = {"x": (1, 2), "y": (0, 2), "z": (0, 1)}   # the two axes across a chute along each axis, as its profile lists them


def chute_profile(els, part, axis, cell):
    """A chute section's walls, cell-local, across its run (over ACROSS[axis]), sorted; with the sets of its
    walls' near and far ends along the run, cell-local."""
    out, near, far = [], set(), set()
    k = "xyz".index(axis)
    a, b = ACROSS[axis]
    o = [cell[i] * B for i in range(3)]
    for el in els:
        if el.part != part:
            continue
        lo, hi = el.aabb()
        out.append(tuple(round(v, 4) for v in (lo[a] - o[a], hi[a] - o[a], lo[b] - o[b], hi[b] - o[b])))
        near.add(round(lo[k] - o[k], 4))
        far.add(round(hi[k] - o[k], 4))
    return sorted(out), near, far


def vanilla_profile(axis):
    """VANILLA_CHUTE's walls as a chute along `axis` has them, over ACROSS[axis], sorted."""
    if axis == "x":
        return sorted(VANILLA_CHUTE["walls"])
    if axis == "y":
        return sorted((16.0 - y1, 16.0 - y0, z0, z1) for y0, y1, z0, z1 in VANILLA_CHUTE["walls"])
    return sorted((16.0 - z1, 16.0 - z0, y0, y1) for y0, y1, z0, z1 in VANILLA_CHUTE["walls"])


def read_vanilla_chute():
    """The game's own straight chute, if the game is here ($VINTAGE_STORY or ~/Games/vintagestory): its
    walls' sections over (y, z), the runs along x, and its texture, or None. The generator holds
    VANILLA_CHUTE to it."""
    for base in (os.environ.get("VINTAGE_STORY"), str(Path.home() / "Games" / "vintagestory")):
        if not base:
            continue
        f = Path(base) / "assets" / "survival" / VANILLA_CHUTE["shape"]
        if f.exists():
            shape = json.loads(f.read_text())
            walls = sorted((e["from"][1], e["to"][1], e["from"][2], e["to"][2]) for e in shape["elements"])
            runs = {(e["from"][0], e["to"][0]) for e in shape["elements"]}
            tex = "game:" + next(iter(shape["textures"].values()))
            return walls, runs, tex
    return None


# ---------------------------------------------------------------- builders: the frame
def trestle(zc, xs, beam_top, leg_top, name):
    """A trestle across the machine at z zc: two oak legs from the ground, their inner faces on x = xs[0]
    and xs[1] (the box they carry, which they rise beside to `leg_top`), and a cross beam under it, its top
    at `beam_top`, mortised through the legs to their middles."""
    f = "frame"
    w0, w1 = xs[0] - LEG, xs[1] + LEG
    return [box([w0, 0.0, zc - LEG / 2], [xs[0], leg_top, zc + LEG / 2], f"{name}_legw", f, "oak"),
            box([xs[1], 0.0, zc - LEG / 2], [w1, leg_top, zc + LEG / 2], f"{name}_lege", f, "oak"),
            box([w0 + LEG / 2, beam_top - BEAM_H, zc - BEAM_D / 2], [w1 - LEG / 2, beam_top, zc + BEAM_D / 2], f"{name}_beam", f, "oak")]


def riser_span():
    """The riser's outside across (x0, x1, z0, z1): the pipe turned up through the head box's floor."""
    return (WATER_IN[0] - PIPE_HALF, WATER_IN[0] + PIPE_HALF, RISER_Z - PIPE_HALF, RISER_Z + PIPE_HALF)


def build_head_box():
    """The head box: a plank box round the infeed chute's mouth, open at the top, standing on two bearers
    along its sides, each mortised into two corner legs that rise beside its walls; the water pipe runs in
    under it, between the bearers, and turns up through its floor. Its south wall stops at the sill (the
    sluice's head floor) across the channel, so the pulp and water in it overflow onto the sluice. Its floor
    lies a voxel under the sill: the pool the ore falls into and the water stirs."""
    f = "frame"
    x0, x1 = HB_X
    z0, z1 = HB_Z
    y0, y1 = HB_Y
    t = HB_T
    rx0, rx1, rz0, rz1 = riser_span()
    out = [# the floor, round the riser
           box([x0 + t, y0, z0 + t], [rx0, HB_FLOOR, z1 - t], "fr_hb_floor_w", f, "planks", along_z=True),
           box([rx1, y0, z0 + t], [x1 - t, HB_FLOOR, z1 - t], "fr_hb_floor_e", f, "planks", along_z=True),
           box([rx0, y0, rz1], [rx1, HB_FLOOR, z1 - t], "fr_hb_floor_s", f, "planks", along_z=True),
           box([x0, y0, z0 + t], [x0 + t, y1, z1 - t], "fr_hb_wall_w", f, "planks", along_z=True),
           box([x1 - t, y0, z0 + t], [x1, y1, z1 - t], "fr_hb_wall_e", f, "planks", along_z=True),
           box([x0, y0, z0], [x1, y1, z0 + t], "fr_hb_wall_n", f, "planks"),
           # the south wall: full height beside the channel, the sill across it
           box([x0, y0, z1 - t], [HB_NOTCH[0], y1, z1], "fr_hb_wall_sw", f, "planks"),
           box([HB_NOTCH[1], y0, z1 - t], [x1, y1, z1], "fr_hb_wall_se", f, "planks"),
           box([HB_NOTCH[0], y0, z1 - t], [HB_NOTCH[1], SL_HEAD_Y, z1], "fr_hb_sill", f, "planks")]
    legs_x = ((x0 - LEG, x0, "w"), (x1, x1 + LEG, "e"))
    legs_z = ((z0, z0 + LEG, "n"), (z1 - LEG, z1, "s"))
    for lx0, lx1, sx in legs_x:
        for lz0, lz1, sz in legs_z:
            out.append(box([lx0, 0.0, lz0], [lx1, HB_LEG_TOP, lz1], f"fr_hb_leg_{sz}{sx}", f, "oak"))
    # the bearers, along z under the side walls, mortised into the legs to their middles: clear of the pipe
    for bx0, bx1, sx in ((x0 - LEG / 2, rx0 - 0.4, "w"), (rx1 + 0.4, x1 + LEG / 2, "e")):
        out.append(box([bx0, y0 - BEAM_H, z0 + LEG / 2], [bx1, y0, z1 - LEG / 2], f"fr_hb_bearer_{sx}", f, "oak"))
    return out


def slot_spans():
    """Each draw slot's (s from, s to) along the floor: right behind riffles 1..6."""
    rz = riffle_zs()
    return [(s_at(rz[i][1]), s_at(rz[i][1]) + SLOT) for i in range(RIFFLE_N - 1)]


def build_sluice():
    """The sluice box: a plank floor between two plank sides, falling 1 in 12 from the head box's sill to
    the tailings chute's mouth; the floor in lengths between the draw slots, the sides in three boards'
    lengths butted over the two trestles, whose legs rise beside them. Every piece's ends are cut square to
    the slope: at the head its top corner meets the head box's south wall (the rest of the end runs into the
    wall), at the tail its bottom corner meets the chute's mouth (the rest runs into the chute's walls), so
    neither end shows a gap."""
    f = "frame"
    floor0 = s_end_at(SL_Z[0], 0.0)
    floor1 = s_end_at(SL_Z[1], -SL_FLOOR_T)
    side0 = s_end_at(SL_Z[0], SL_SIDE_H)
    side1 = s_end_at(SL_Z[1], -SL_FLOOR_T)
    xw, xe = (SL_IN[0] - SL_SIDE_T, SL_IN[0]), (SL_IN[1], SL_IN[1] + SL_SIDE_T)
    pieces = []
    edges = [floor0] + [v for a, b in slot_spans() for v in (a, b)] + [floor1]
    for i in range(0, len(edges), 2):
        pieces.append((edges[i], edges[i + 1], -SL_FLOOR_T, 0.0, SL_IN[0], SL_IN[1], f"fr_sl_floor{i // 2 + 1}", "planks"))
    joints = [side0] + [s_at(z) for z in SL_SPLITS] + [side1]
    for i, (a, b) in enumerate(zip(joints, joints[1:]), 1):
        pieces.append((a, b, -SL_FLOOR_T, SL_SIDE_H, *xw, f"fr_sl_side_w{i}", "planks"))
        pieces.append((a, b, -SL_FLOOR_T, SL_SIDE_H, *xe, f"fr_sl_side_e{i}", "planks"))
    out = sloped(pieces, f)
    xs = (xw[0], xe[1])
    for i, zc in enumerate(SL_TRESTLES_Z, 1):
        top = under_slope(zc - BEAM_D / 2, zc + BEAM_D / 2, LD_H[0])
        leg_top = sl_floor(zc + LEG / 2) + SL_SIDE_H - SL_LEG_DROP
        out += trestle(zc, xs, top, leg_top, f"fr_sl_trestle{i}")
    return out


def build_launder():
    """The concentrate launder: a closed plank trough slung under the sluice's floor at its grade, its sides
    under the sluice's sides and its inside the channel's width, so each draw slot drops straight into it;
    closed at its head, open at its end over the concentrate box. Two boards' lengths."""
    f = "frame"
    s0, s1 = s_at(LD_Z[0]), s_at(LD_Z[1])
    sm = s_at(LD_SPLIT_Z)
    bottom, top = LD_H
    pieces = []
    for i, (a, b) in enumerate(((s0, sm), (sm, s1)), 1):
        pieces.append((a, b, bottom, bottom + LD_T, SL_IN[0], SL_IN[1], f"fr_ld_floor{i}", "planks"))
        pieces.append((a, b, bottom, top, LD_OUT[0], SL_IN[0], f"fr_ld_side_w{i}", "planks"))
        pieces.append((a, b, bottom, top, SL_IN[1], LD_OUT[1], f"fr_ld_side_e{i}", "planks"))
    pieces.append((s0, s0 + LD_T, bottom + LD_T, top, SL_IN[0], SL_IN[1], "fr_ld_head", "planks"))
    return sloped(pieces, f)


def build_conc_box():
    """The concentrate box under the tail: a plank box the concentrate chute's bore wide, open on its west
    side into the chute, its floor the chute's bore floor; the launder pours into it over its north wall.
    Its south wall rises to the sluice's floor, closing the tailings chute's mouth under the lip. On two
    oak sleepers."""
    f = "frame"
    x0, x1 = CB_X
    z0, z1 = CB_Z
    y0, t = CB_Y0, CB_T
    south_top = under_slope(z1 - t, z1, -SL_FLOOR_T)
    out = [box([x0, y0, z0 + t], [x1 - t, y0 + t, z1 - t], "fr_cb_floor", f, "planks"),
           box([x0, y0, z0], [x1, CB_TOP, z0 + t], "fr_cb_wall_n", f, "planks"),
           box([x1 - t, y0, z0 + t], [x1, CB_TOP, z1 - t], "fr_cb_wall_e", f, "planks", along_z=True),
           box([x0, y0, z1 - t], [x1, south_top, z1], "fr_cb_wall_s", f, "planks")]
    for i, (a, b) in enumerate(CB_SLEEPERS_Z, 1):
        out.append(box([CB_SLEEPER_X[0], 0.0, a], [CB_SLEEPER_X[1], y0, b], f"fr_cb_sleeper{i}", f, "oak"))
    return out


def build_frame():
    return build_head_box() + build_sluice() + build_launder() + build_conc_box()


# ---------------------------------------------------------------- builders: the fitted parts
def riffle_zs():
    """Each riffle's (upstream face, downstream face) over the floor's top line, z."""
    return [(RIFFLE_Z0 + RIFFLE_PITCH * i, RIFFLE_Z0 + RIFFLE_PITCH * i + RIFFLE_T) for i in range(RIFFLE_N)]


def build_riffles():
    """The riffles (`riffles`): seven oak bars across the channel, standing on the floor square to the
    slope, each faced on top with a strap of iron that overhangs its downstream face, over its draw slot."""
    p = "riffles"
    pieces = []
    for i, (zu, zd) in enumerate(riffle_zs(), 1):
        su, sd = s_at(zu), s_at(zd)
        pieces.append((su, sd, 0.0, RIFFLE_H, SL_IN[0], SL_IN[1], f"riffle_{i}_bar", "oak"))
        pieces.append((su, sd + STRAP_LIP, RIFFLE_H, RIFFLE_H + STRAP_T, SL_IN[0], SL_IN[1], f"riffle_{i}_strap", "iron"))
    return sloped(pieces, p)


def pockets():
    """Each pocket's (s from, s to): behind riffle i (its downstream face) up to riffle i + 1 (its upstream
    face), six of them, each over its draw slot."""
    rz = riffle_zs()
    return [(s_at(rz[i][1]), s_at(rz[i + 1][0])) for i in range(RIFFLE_N - 1)]


def bed_pieces():
    """The concentrate's bed in each pocket while the sluice runs: a wedge in two steps against the riffle,
    over its draw slot, deepest against the riffle: a lower layer most of the way along and a deeper one half
    as long (and over the whole slot). Each runs a hair into the floor and the sides (and the riffle), so no gap shows; the two steps'
    hidden faces are staggered so none shares a plane."""
    out = []
    for i, ((a, b), f) in enumerate(zip(pockets(), BED), 1):
        name = f"conc_pocket{i}"
        length = (b - a) * (0.3 + 0.7 * f)
        lo = CONC_IN + 0.01
        out.append((a - lo, a + length, -lo, CONC_FULL * f * 0.5, SL_IN[0] - lo, SL_IN[1] + lo, f"{name}a", "concentrate"))
        deep = max(length * 0.5, SLOT + 0.15)     # the deeper step covers the draw slot under it
        out.append((a - CONC_IN, a + deep, -CONC_IN, CONC_FULL * f, SL_IN[0] - CONC_IN, SL_IN[1] + CONC_IN, f"{name}b", "concentrate"))
    return out


def stream_pieces():
    """The concentrate drawn off: through each slot and down onto the launder's floor (a sheet the slot's
    length, a hair narrower than the channel), and the layer running down the launder's floor to its end."""
    out = []
    floor_top = LD_H[0] + LD_T
    for i, (a, b) in enumerate(slot_spans(), 1):
        out.append((a + 0.02, b - 0.02, floor_top - 0.02, 0.0, SL_IN[0] + 0.1, SL_IN[1] - 0.1, f"conc_slot{i}", "concentrate"))
    s0, s1 = s_at(LD_Z[0]) + LD_T, s_at(LD_Z[1])
    sm = s_at(LD_SPLIT_Z) + 1.5                  # its two lengths butted away from the launder's boards' joint
    for i, (a, b) in enumerate(((s0, sm), (sm, s1 - 0.02)), 1):
        out.append((a, b, floor_top - 0.03, floor_top + CONC_LAYER, SL_IN[0] - 0.04, SL_IN[1] + 0.04, f"conc_launder{i}", "concentrate"))
    return out


def build_concentrate():
    """The concentrate (`concentrate`), drawn while the sluice runs: the beds behind the riffles, the sheets
    falling through the slots, the layer down the launder and the concentrate lying in the box, running out
    into the chute."""
    out = sloped(bed_pieces() + stream_pieces(), "concentrate")
    x0, x1 = CB_X
    z0, z1 = CB_Z
    top = CB_Y0 + CB_T
    out.append(box([x0 + 0.2, top - 0.03, z0 + CB_T - 0.04], [x1 - CB_T + 0.04, top + CONC_LAYER, z1 - CB_T + 0.04], "conc_box", "concentrate",
                   "concentrate"))
    return out


def build_chutes():
    """The infeed (`infeedchute`): vanilla's straight chute, upright, from the head cell's top face down into
    the head box, held by the box's walls round it. The tailings chute (`tailingschute`): the same section
    running south from the sluice's tail to the tail cell's south face. The concentrate chute
    (`concentratechute`): the same section running west from the concentrate box's open side to the tail
    cell's west face."""
    top = (INFEED_CELL[1] + 1) * B
    end = (TAILINGS_CELL[2] + 1) * B
    return (chute_walls("y", CHUTE_IN_Y0, top, INFEED_CELL, "infeedchute", "infeedchute")
            + chute_walls("z", CHUTE_OUT_Z0, end, TAILINGS_CELL, "tailingschute", "tailingschute")
            + chute_walls("x", CONC_CELL[0] * B, CHUTE_CONC_X1, CONC_CELL, "concentratechute", "concentratechute"))


def pipe_boxes():
    """The water inlet as UnifiedPipes' soldered pipe (solderedpipe's boxes, build-frame voxels): a straight
    length from the water face, on the axis of the ppex pipe beyond it, south under the head box between its
    bearers, to a bend (a hub, as UnifiedPipes' soldered bend) that turns it up through the box's floor; on
    the riser's end, on the floor, a reducer plate and the nozzle in two steps from its middle, pointing up
    into the pool. As in the rosser's drip, the inlet has no joint at the face: the pipe beyond brings its own."""
    x, y = WATER_IN
    h = PIPE_HALF
    out = solderedpipe.tube("inlet", 2, 0.0, RISER_Z - h, (x, y))
    out += solderedpipe.hub("bend", (x, y, RISER_Z), ("north", "up"))
    out += solderedpipe.tube("riser", 1, y + h, HB_FLOOR, (x, RISER_Z))
    out.append(solderedpipe.Box("reducer", [x - h, HB_FLOOR, RISER_Z - h], [x + h, HB_FLOOR + REDUCER_T, RISER_Z + h], solderedpipe.BODY))
    y0 = HB_FLOOR + REDUCER_T
    for i, (half, height) in enumerate(NOZZLE, 1):
        out.append(solderedpipe.Box(f"nozzle{i}", [x - half, y0, RISER_Z - half], [x + half, y0 + height, RISER_Z + half], solderedpipe.BODY))
        y0 += height
    return solderedpipe.cull_pressed(out)


def nozzle_mouth():
    return HB_FLOOR + REDUCER_T + sum(hh for _, hh in NOZZLE)


def build_pipes(metal_=PIPE_METALS[0]):
    """The water inlet as elements of the part `pipe<metal_>` (`pipe_boxes`): the body in that metal's pipe
    texture, each face's UVs solderedpipe's on this model's texture scale. Built in copper; `pipe_copies`
    makes lead."""
    p = f"pipe{metal_}"
    codes = {solderedpipe.BODY: f"#{p}", solderedpipe.SOLDER: "#solder"}
    out = []
    for b in pipe_boxes():
        size = b.size()
        faces = {d: {"texture": codes[b.role], "uv": solderedpipe.uv(d, size, TEX / 16)} for d in b.faces}
        out.append(El(f"{p}_{b.name}", size, [(b.lo[k] + b.hi[k]) / 2 for k in range(3)], [row[:] for row in IDENT], faces, p))
    return out


def pipe_copies(els):
    """The water inlet in the other metals: the first metal's elements as they are (after the z-fighting
    fix, so every copy has the same insets), renamed and in their own metal's texture. Only one metal's are
    ever drawn, so the copies are not checked against each other."""
    first = PIPES[0]
    out = []
    for m in PIPE_METALS[1:]:
        for el in els:
            if el.part == first:
                c = el.clone(f"pipe{m}" + el.name[len(first):], f"pipe{m}")
                for face in c.faces.values():
                    if face["texture"] == f"#{first}":
                        face["texture"] = f"#pipe{m}"
                out.append(c)
    return out


def build():
    return build_riffles() + build_concentrate() + build_chutes() + build_pipes() + build_frame()


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


CHUTES = ("infeedchute", "tailingschute", "concentratechute")


def _rig_parts():
    parts = [
        # nothing moves: every part is fitted, or drawn while the sluice runs, with no driver
        {"id": "riffles", "match": ["riffle_*"], "requires": "riffles", "drivers": []},
        {"id": "concentrate", "match": ["conc_*"], "requires": "concentrate", "drivers": []},
        *[{"id": c, "match": [f"{c}_*"], "requires": c, "drivers": []} for c in CHUTES],
        # the water inlet, one part per metal: only the supplied metal's is drawn
        *[{"id": pid, "match": [f"{pid}_*"], "requires": pid, "drivers": []} for pid in PIPES],
        {"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []},
    ]
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# ---------------------------------------------------------------- poses: what is shown
# A pose is (concentrate shown 0 or 1, pipe part): nothing moves, so a pose only says which parts show; every
# part's matrix is the identity.
REST = (0, PIPES[0])
POSES = tuple((k, pid) for k in (0, 1) for pid in PIPES)


def inputs_of(pose):
    return {"theta": 0.0}


def pm(parts, pid, pose):
    return _part_matrix(parts, pid, inputs_of(pose), None)


def on_show(part, pose):
    """Whether a part shows in a pose: the concentrate only while it runs, each pipe metal only as itself."""
    shown, pipe = pose
    if part == "concentrate":
        return bool(shown)
    if part in PIPES:
        return part == pipe
    return True


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


POINTS = ("feedSpout", "waterSpout", "concentrateSpout")
FACE_ANCHORS = ("infeed", "water", "tailings", "concentrate")


def launder_lip():
    """The launder's floor's top at its open end, over the concentrate box (build frame)."""
    return slope_point(s_at(LD_Z[1]), LD_H[0] + LD_T)


def make_rig(parts):
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, the controller [0,0,0]: the head cell, nearest the "
                    "player; the sluice runs south from it, three blocks. Nothing moves: no power cell, no work, no driver. "
                    "infeedCell/infeedFace: a vanilla chute from above meets the infeed section; waterCell/waterFace: a ppex "
                    "pipe meets the water inlet; tailingsCell/tailingsFace: the tailings chute leaves the tail; "
                    "concentrateCell/concentrateFace: the concentrate chute leaves the concentrate box, which the launder under "
                    "the riffles fills through their draw slots as the sluice runs. feedSpout is where the ore falls into the "
                    "head box, waterSpout the nozzle's mouth, concentrateSpout where the launder pours into the concentrate "
                    "box. See the sluice's README for the schema.",
        "cells": [],
        "infeedCell": list(INFEED_CELL),
        "infeedFace": INFEED_FACE,
        "waterCell": list(WATER_CELL),
        "waterFace": WATER_FACE,
        "tailingsCell": list(TAILINGS_CELL),
        "tailingsFace": TAILINGS_FACE,
        "concentrateCell": list(CONC_CELL),
        "concentrateFace": CONC_FACE,
        "feedSpout": {"pos": pt(CX, CHUTE_IN_Y0, (HB_Z[0] + HB_Z[1]) / 2)},
        "waterSpout": {"pos": pt(CX, nozzle_mouth(), RISER_Z)},
        "concentrateSpout": {"pos": pt(*launder_lip())},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0] (here no move: the build frame's
    corner is the controller's)."""
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    db = [v / B for v in d]
    ship_els = copy.deepcopy(els)
    translate(ship_els, d)
    ship_parts = copy.deepcopy(parts)
    ship = copy.deepcopy(rig)
    for key in FACE_ANCHORS:
        ship[key + "Cell"] = shift_cell(rig[key + "Cell"], ORIGIN_CELL)
    for key in POINTS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, every part shown (each pipe metal is the same
    geometry), at rest: what every reader rebuilds the same way. No lids: the channel is narrower than a
    player, and the boxes are the deck."""
    written = flatten(shape["elements"], textures={})
    rest = [posed(w, _part_matrix(ship_parts, part_of(ship_parts, w.name), inputs_of(REST), None)) for w in written]
    by_cell = {}
    for el in rest:
        lo, hi = el.aabb()
        for c in cells_touched(lo, hi):
            by_cell.setdefault(c, []).append(el)
    out = []
    for c in footprint():
        pos = tuple(c[k] - ORIGIN_CELL[k] for k in range(3))
        boxes = cell_boxes(by_cell[pos], pos) if pos in by_cell else None
        out.append({"pos": list(pos), "boxes": boxes} if boxes else {"pos": list(pos), "hollow": True})
    return out


def check_shipped(els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose), None), d, POSES)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; cells {cells}")
    if worst > 1e-6 or cells != footprint():
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. The frame (fr_*: the head box, "
             "the sluice box, the concentrate launder under it, the concentrate box, the trestles), the fitted parts "
             "(riffle_*: the riffles; infeedchute_* / tailingschute_* / concentratechute_*: vanilla's chute section, in and "
             "the two outputs; pipecopper_* / pipelead_*: the water inlet in each metal) and the concentrate drawn while the "
             "sluice runs (conc_*). The concentrate's texture code is 'concentrate', so a renderer can set it to the ore's. "
             "Keep element names when editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


def coplanar_poses():
    return POSES[::2]                        # with and without the concentrate, the copper pipe (lead is the same elements)


def shown(posed_els, pose):
    """The posed elements as they can be seen in a pose: the parts not shown moved far away (copies, in
    order), so the z-fighting fix and its check deal only with faces that show together."""
    out = []
    for e in posed_els:
        if not on_show(e.part, pose):
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 - 10.0 * len(out), e.c[2]]
        out.append(e)
    return out


def fix_coplanar(els, parts):
    return fix_coplanar_posed(els, lambda es, pose: shown([posed(el, pm(parts, el.part, pose)) for el in es], pose),
                              coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the sluice's shapes and rig.")
    ap.add_argument("--out", type=Path, help="write the three files into this directory instead of the mod's assets")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and its check (not for files that ship)")
    args = ap.parse_args()
    import validate_sluice
    els = build()
    for el in els:
        el.r = [[0.0 if abs(v) < 1e-9 else (math.copysign(1.0, v) if abs(abs(v) - 1) < 1e-9 else v) for v in row] for row in el.r]
    parts = rig_parts()
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        for pose, pairs in before.items():
            print(f"coplanar faces before the fix at {pose}: {len(pairs)} pairs")
        print(f"coplanar faces: {hidden} faces pressed against their own part removed")
    els += pipe_copies(els)
    rig = make_rig(parts)
    ok = validate_sluice.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "sluice.json", args.out / "sluice_frame.json", args.out / "sluice-rig.json")
    else:
        outs = (SHAPE_DIR / "sluice.json", SHAPE_DIR / "sluice_frame.json", RIG_DIR / "sluice-rig.json")
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_sluice.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
    texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship))
    for path, text in zip(outs, texts):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
        json.loads(path.read_text())
        print(f"wrote {path} ({path.stat().st_size // 1024} KiB)")
    if not (check_shipped(els, parts, ship_els, ship_parts, ship) and ok):
        sys.exit(1)
    print("all checks passed")


if __name__ == "__main__":
    main()
