#!/usr/bin/env python3
"""Generate the concentrator's (wash house's) shapes, rig and reference poses.

The concentrator is the ore line's gravity concentration stage (#734), one machine upgraded in place
(#711): a timber wash house, built at its final size, into which each level's parts are fitted. A level
adds parts to the last; only the sluice is retired, when the jig takes its place at the head of the line.

    level 1 (water)  sluice    a riffled sluice under the feed box, fed by the player, on process water;
                               no power: the player cleans its riffles up by hand
    level 2 (water)  jig       the line shaft from the axle, and a Hartz jig: a hutch with a sieve
                               compartment and a plunger compartment, the plunger worked by an eccentric
                               on the line shaft; its tailings overflow a weir, its concentrate is drawn
                               off at a spigot. The sluice is taken out: the jig stands in its place
    level 3 (mill)   table     a shaking table taking the jig's tailings: a riffled deck shaken along its
                               length by an eccentric on the line shaft, its concentrate off the far end
                               and its tailings off the low edge
    level 4 (mill)   vanner    a Frue vanner taking the table's tailings: an endless belt on a frame
                               shaken sideways from a countershaft belted from the line shaft, crept up to
                               its head by a worm whose shaft a ratchet turns, a pawl on the shaking frame
                               pushing it a tooth a stroke, so it creeps the right way whichever way the
                               axle turns. Its water is recirculated, a gameplay rule: nothing in
                               the machine carries it back (the owner's ruling); the tail box's overflow is
                               an outlet anchor of its own beside the tailings' spigot

From level 2, amalgamation plates (`plates`, one fitted part, not a level) can lie in the plate table the
concentrate runs over on its way out, bare or dressed with mercury (`dressing`).

Every power take-off is the same: an eccentric sheave keyed on a shaft, running in an iron yoke (a slotted
crosshead, the Scotch yoke's way), so each motion is exactly a sine of the shaft's angle and looks the same
either way the axle turns; the vanner's creep alone needs a direction, and its ratchet gives it one. The
water line, from the one inlet to its three outlets, is Pipes and Power Expanded's straight pipe as
UnifiedPipes draws it in copper and lead, soldered (Pipes/tools/solderedpipe.py, as the rosser's drip): one
rig part per metal, `pipecopper` and `pipelead`, the same elements, only the supplied metal's drawn.
Everything else is built here from plain boxes; no other mod's model is used.

It writes, deterministically,

    concentrator.json         the whole machine, every level's parts      (assets/.../shapes/block/)
    concentrator_frame.json   the wash house frame only (block and item)   (assets/.../shapes/block/)
    concentrator-rig.json     cells, anchors and the part rig              (assets/.../config/)
    rig-reference.json        every part's matrix at a spread of poses     (tests/Concentrator/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_concentrator.py) and exits
non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the
machine box's north-west-bottom corner (the "build frame"); the controller cell is the build frame's
[0,0,0], so the shipped files are the build frame divided by 16. The line shaft runs along x, from the
power face at the west end.

The rig's inputs (README "Rig"):

    theta   the axle's angle: the line shaft turns with it, and everything is driven from the line shaft
    travel  psi, the axle's travel either way: the ratchet, the worm and the vanner's rollers, which only
            ever turn forward
"""

from __future__ import annotations

import argparse
import copy
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Machines" / "tools"))
sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Pipes" / "tools"))

from machinegen.checks import cell_boxes, cells_touched, with_lids  # noqa: E402
from machinegen.checks import fix_coplanar as fix_coplanar_posed  # noqa: E402
from machinegen.geometry import IDENT, El, flatten, rotate, translate  # noqa: E402
from machinegen.output import (reference_dumps, rig_dumps, round_matrix, shape_dumps, shift_cell,  # noqa: E402
                               shift_point, worst_shift_error)
from machinegen.output import shape_json as machine_shape_json  # noqa: E402
from machinegen.rigmath import part_of, posed, validate_driver  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402

import solderedpipe  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
REFERENCE_OUT = MOD / "tests" / "Concentrator" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/Concentrator/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi
DEG = math.pi / 180

# ---------------------------------------------------------------- the box, the cells, the levels
CELLS_X, CELLS_Y, CELLS_Z = 4, 2, 3          # four long (x, the line shaft's way), two high, three wide
X_LEN, Y_LEN, Z_LEN = CELLS_X * B, CELLS_Y * B, CELLS_Z * B
ORIGIN_CELL = (0, 0, 0)                      # the controller: the north-west corner, under the feed box
POWER_CELL, POWER_FACE = (0, 1, 1), "west"   # the vanilla axle comes in along x at (y 24, z 24)
WATER_CELL, WATER_FACE = (1, 1, 0), "north"  # a ppex pipe meets the inlet at (x 24, y 24) on the north face
FEED_CELL, FEED_FACE = (0, 1, 0), "north"    # a chute pours into the feed box through the north face

LEVELS = (                                   # (level, tier, what it fits, its name)
    (1, "water", ("sluice",), "riffled sluice"),
    (2, "water", ("lineshaft", "jig"), "line shaft and Hartz jig (the sluice taken out)"),
    (3, "mill", ("lineshaft", "jig", "table"), "jig and shaking table"),
    (4, "mill", ("lineshaft", "jig", "table", "vanner"), "jig, table and vanner (water recirculated: a gameplay rule)"),
)
REQUIRES = ("sluice", "lineshaft", "jig", "table", "vanner")
PLATES = ("plates", "dressing")              # the amalgamation plates and their mercury dressing
PLATES_FROM = 2
PIPE_METALS = ("copper", "lead")             # the water line's metals: one part each, the supplied one drawn
PIPES = tuple(f"pipe{m}" for m in PIPE_METALS)

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",         # timber: posts, beams, sills, braces, trestles, legs, rollers
    "planks": "game:block/wood/planks/oak1",       # boards: the sluice, the hutch, the deck, launders, boxes
    "iron": "game:block/metal/plate/iron",         # castings and straps: yokes, bearings, hangers, brackets, spigots
    "steel": "game:block/metal/sheet-plain/steel1",  # shafts, sheaves, pulleys, rods, the worm and its wheel
    "screen": "game:block/metal/mesh1",            # the jig's sieve
    "leather": "game:block/leather/plain",         # the belts
    "pipecopper": "game:block/metal/sheet-plain/copper4",   # the water line in copper (UnifiedPipes' copper pipe)
    "pipelead": "game:block/metal/sheet-plain/lead4",       # ... in lead (UnifiedPipes' lead pipe)
    "solder": solderedpipe.SOLDER_TEXTURE,         # the pipes' joints
    "copper": "game:block/metal/sheet-plain/copper1",  # the amalgamation plates, bare
    "mercury": "seraphhorizons:block/liquid/mercury",  # their dressing (the amalgam pan's mercury texture)
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the wash house frame
POST = 3.0                                   # posts and beams 3 x 3
PLATE_Y = (29.0, 32.0)                       # the wall plates and the tie beams on them
MID_X = (30.5, 33.5)                         # the long walls' middle posts and the middle tie beam
SILL_Y = 2.5
BRACE = 9.0                                  # knee braces: 9 down the post, 9 along the beam
BRACE_W = 2.0

# ---------------------------------------------------------------- the line shaft (level 2 on)
LS = (24.0, 24.0)                            # (y, z): the line shaft along x, at the power cell's centre
LS_R = 0.8                                   # its radius (steel, an octagon)
LS_X = (2.5, 61.4)
ENTRY_X = 2.4                                # the axle's oak cross runs from the power face to here
HANGERS = ((3.0, 4.8), (30.9, 33.1), (59.6, 61.4))   # the three bearings, hung from the tie beams
BEARING_H = 2.0                              # each bearing block is 4 x 4 about the shaft
SHEAVE_R = 1.75                              # every eccentric sheave (a 12-sided disc, apothem)
SHEAVE_RC = SHEAVE_R / math.cos(math.pi / 12)    # its corners: the yokes clear them
SHEAVE_W = 1.4                               # its thickness along the shaft
YOKE_T = 0.7                                 # the yokes' bars
YOKE_GAP = 0.3                               # a yoke's end bars clear the sheave's swing by this
JIG_X, TABLE_X = 10.0, 20.2                  # the sheaves on the line shaft
JIG_E, TABLE_E = 0.6, 0.5                    # their throws: the plunger's stroke 1.2, the deck's 1.0
VPULLEY_X = (17.6, 19.0)                     # the vanner's drive pulley on the line shaft (and on its countershaft)
VPULLEY_R = 3.0

# ---------------------------------------------------------------- the fixed points (the frame's)
# the feed box: against the north wall under the plate, a spout south to the head of the line
FB_X = (4.0, 12.0)
FB_Z = (0.3, 7.7)
FB_Y = (25.5, 29.0)
FS_X = (6.0, 10.0)                           # its spout trough
FS_Z = (7.7, 11.5)
FS_Y = 25.0                                  # the spout's floor's underside; its floor's top is 25.6
FEED_LIP = (8.0, FS_Y, FS_Z[1])              # where the feed falls from, onto the sluice's head or the jig's sieve
# the water line: ppex's straight pipe, soldered (solderedpipe: a square tube 6 across round a 4 x 4 bore).
# The inlet runs south from the water face to a tee; west from it to the head of the line, east along the
# house and down under the line shaft, then south to the vanner; a nozzle under each outlet.
PIPE_HALF = solderedpipe.HALF                # 3: ppex's pipe is 6 across
WATER_IN = (24.0, 24.0)                      # (x, y): the inlet's axis on the water cell's north face
TEE_Z = 13.0                                 # the tee, where the inlet meets the cross line
HEAD_OUT_X = 14.0                            # the head of the line's outlet (the sluice's head, the jig's sieve)
TABLE_OUT_Z = 14.9                           # the table's nozzle, under the tee, over its water launder
VAN_X = 45.5                                 # the vanner's branch: down at x 45.5 ...
VAN_Y = 16.5                                 # ... to run south under the line shaft at y 16.5 ...
VAN_Z1 = 41.1                                # ... to its capped end over the belt's south edge
VAN_SEAM = 30.0                              # the seam (a joint) between its two lengths
VAN_NOZZLES_Z = (36.0, 37.5, 39.0)           # its nozzles over the belt, inside its raised edges
NOZZLE_Y = 1.0                               # a nozzle hangs this far under its pipe
WATER_LIP = (HEAD_OUT_X, WATER_IN[1] - PIPE_HALF - NOZZLE_Y, TEE_Z)   # the head of the line's nozzle's mouth
# the tailings: launder N (the north side) and launder S (the south side) into the tail box at the east end
LN_X = (18.0, 55.8)
LN_Z = (8.5, 15.5)
LN_Y = (3.4, 2.0)                            # its floor's top at the west end and the east end
LS_LAUNDER_X = (17.4, 55.8)
LS_LAUNDER_Z = (36.0, 41.6)
LS_LAUNDER_Y = (1.4, 0.8)
LAUNDER_T = 0.6
TB_X = (54.5, 62.0)                          # the tail box: a long box across the east end
TB_Z = (4.0, 44.0)
TB_Y = 6.5                                   # its walls' top
TB_FLOOR = 0.8
TAIL_OUT_Z = (34.5, 37.5)                    # its spigot (the settled tailings) out through the east wall
TAIL_OUT_Y = (1.0, 2.2)
OVERFLOW_Z = (11.0, 14.0)                    # its overflow (the clear water) over a notch in the east wall
OVERFLOW_Y = 5.6                             # the notch's sill: the box's water level
RETURN_CELL, RETURN_FACE = (3, 0, 0), "east"  # where the return water leaves the machine (the gameplay recirculates it)
# the concentrate: the concentrate launder along the middle, onto the plate table at the west end
CL_X = (12.0, 50.5)
CL_Z = (28.0, 32.0)
CL_Y = (3.1, 3.6)                            # its floor's top at the west end (open, over the plates) and the east end
CL_SIDE = 1.0
PT_X = (2.0, 14.0)                           # the plate table: a plank bed falling south, 1 in 8
PT_Z = (28.0, 45.0)
PT_Y = (2.1, 0.75)                            # its floor's top at the head (z 28.8) and the foot (z 44.2)
PT_T = 0.6
PT_SIDE = 1.4
PT_NOTCH_Z = (42.0, 44.2)                    # the outlet: a notch in the west side at the foot, a spout to the west face
PLATE_Z = [(29.2, 32.9), (33.2, 36.9), (37.2, 40.9)]   # the three copper plates down the bed
PLATE_T = 0.3
TRAP_Z = (41.1, 41.6)                        # the mercury trap: an iron riffle across the bed after the plates
TRAP_H = 0.8
COAT_T = 0.08                                # the mercury dressing on each plate

# ---------------------------------------------------------------- level 1: the sluice
SL_X = (6.0, 40.0)                           # head .. tail (open, over launder N)
SL_Z = (6.5, 15.5)                           # outside; sides 1 thick: the channel 7.5..14.5
SL_HEAD_Y = 18.3                             # its floor's top at the head (under the water line); it falls 1 in 12
SL_SLOPE = 1 / 12
SL_SIDE = 3.0
RIFFLE_XS = [12.0 + 4.0 * i for i in range(7)]   # the riffles across the floor (their downstream faces)
RIFFLE_H = 1.2
RIFFLE_T = 1.0
SL_TRESTLES = (14.0, 38.0)

# ---------------------------------------------------------------- level 2: the jig
JG_X = (3.0, 17.0)                           # the hutch, outside
JG_Z = (3.0, 27.0)
JG_FLOOR = 3.5                               # the hutch floor's top, on three sills
JG_TOP = 20.3
JG_PART_Z = (16.6, 17.6)                     # the partition: the sieve compartment north, the plunger's south
JG_PART_Y0 = 9.0                             # it stops this far up: the water passes under it
SIEVE_Y = 16.5                               # the sieve's top
WEIR_Z = (8.6, 11.9)                         # the overflow weir: a notch in the east wall over the sieve
WEIR_Y = 19.6                                # its sill (the water's level)
SPOUT_X1 = 23.4                              # the weir's spout trough runs east to here, over the table's feed box
PLUNGER_X = (6.5, 13.5)
PLUNGER_Z = (19.5, 25.5)
PLUNGER_Y = (13.8, 15.3)                     # at mid-stroke
PLUNGER_ROD_Z = 22.5
GUIDE_Y = (19.3, 20.3)
SPIGOT_X = (14.4, 15.8)                      # the concentrate spigot out of the south wall, into the concentrate launder
SPIGOT_Y = (4.8, 6.2)

# ---------------------------------------------------------------- level 3: the shaking table
DECK_X = (21.0, 37.0)
DECK_Z = (3.5, 27.0)
DECK_TOP = 16.8                              # the deck's top along its upper (west) edge
DECK_T = 1.0
DECK_TILT = 2.5                              # degrees: the deck falls east, across its width
DECK_LIP = (27.0, 29.5)                      # the concentrate lip past the south end, over the concentrate launder
JOISTS_X = ((23.0, 25.0), (33.0, 35.0))
JOIST_H = 2.0
LEGS_Z = ((5.0, 7.0), (18.6, 20.6))
RIFFLES = ((27.5, 26.5), (30.0, 25.0), (32.5, 23.0), (35.0, 21.0))   # (x, its south end): the riffles along z
DECK_RIFFLE_H = 1.0
FEEDBOX_Z = (8.0, 12.5)
FEEDBOX_X1 = 26.0
FEEDBOX_H = 1.8
WL_X1 = 26.0                                 # the water launder along the upper edge
WL_Z = (13.0, 26.5)
TT_X = (37.4, 40.4)                          # the tails launder along the low edge, falling south to P3
TT_Z = (4.0, 37.8)
TT_Y = (14.6, 14.3)                          # its floor's top at the north end and at its spout
TT_SIDE = 1.0
TT_LEGS_Z = ((4.6, 5.6), (17.0, 18.0), (26.6, 27.6))

# ---------------------------------------------------------------- level 4: the vanner
HEAD = (46.0, 9.1)                           # (x, y): the head roller's axis (the belt rises to the head)
FOOT = (22.0, 8.3)
ROLL_R = 2.5
BELT_T = 0.4
BELT_Z = (33.9, 41.1)
RAILS_Z = ((32.0, 33.5), (41.5, 43.0))
RAIL_Y = (7.0, 10.0)
RAIL_X = (18.0, 49.9)
VN_E = 0.5                                   # the shake: half a voxel either side
CS = (14.1, 43.9)                            # (y, z): the countershaft along x, by the south rail
CS_R = 0.7
CS_X = (17.2, 38.5)                          # the countershaft: the drive pulley, the two sheaves
CS_SHEAVES_X = (25.5, 34.6)
CS_PEDESTALS = ((19.4, 20.8), (28.6, 30.0), (36.9, 38.3), (40.0, 41.4), (50.6, 51.9))   # the last two the worm shaft's
WS_X = (38.7, 52.4)                          # the worm shaft, in line with the countershaft beyond its end
RATCHET_X = (38.8, 39.7)                     # the ratchet wheel on it: 12 teeth, module 0.5
RATCHET_N = 12
RATCHET_MOD = 0.5
WORM_N = 12                                  # the head roller's worm wheel: 12 teeth, module 0.6, a single-start worm
WHEEL_MOD = 0.6
WHEEL_R = WORM_N * WHEEL_MOD / 2             # its pitch radius, 3.6
WORM_R = CS[0] - HEAD[1] - WHEEL_R           # the worm's pitch radius: the countershaft over the wheel's top
WHEEL_Z = (43.2, 44.6)
RATCHET_R = 2.0                              # the ratchet's pitch radius (its teeth module RATCHET_MOD, 2 pi R / N apart)
PAWL_TRAVEL = 2 * VN_E                       # the pawl pushes the ratchet round by the shake's stroke each turn
RATCHET_RATIO = PAWL_TRAVEL / RATCHET_R / (2 * math.pi)   # radians of the ratchet per radian of the axle's travel
TANK_X = (42.0, 50.0)                        # the concentrate box under the head
TANK_Z = (34.3, 41.2)
TANK_Y = (4.8, 5.9)                          # its floor's top and its walls' top
TANK_SPOUT_X = (44.5, 47.5)
VFEED_X = (37.5, 42.5)                       # the vanner's feed box over the belt, under P3
VFEED_Y = (12.3, 13.5)
VSLIDES_X = (25.5, 33.0, 39.5)


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


def beam(lo, hi, name, part, tex, seg=16.0):
    """A long box split into pieces of at most `seg` along its longest axis (so no texture stretches)."""
    axis = max(range(3), key=lambda a: hi[a] - lo[a])
    n = max(1, math.ceil((hi[axis] - lo[axis]) / seg - 1e-9))
    out = []
    for i in range(n):
        l2, h2 = list(lo), list(hi)
        l2[axis] = lo[axis] + (hi[axis] - lo[axis]) * i / n
        h2[axis] = lo[axis] + (hi[axis] - lo[axis]) * (i + 1) / n
        out.append(box(l2, h2, f"{name}{i + 1}" if n > 1 else name, part, tex))
    return out


AX = {"x": 0, "y": 1, "z": 2}
REF = {"x": (1, 2), "y": (2, 0), "z": (1, 0)}   # a disc's angle 0 direction, and the in-plane direction it turns towards


def radial(axis, c, a0, a1, r0, r1, width, ang, name, part, tex):
    """A box from radius r0 to r1 about `axis` through c, a0..a1 along the axis, `width` across, at angle
    `ang` (radians, right-handed about +axis from the axis's reference direction)."""
    a = AX[axis]
    u, w = REF[axis]
    lo, hi = [0.0] * 3, [0.0] * 3
    lo[a], hi[a] = a0, a1
    lo[u], hi[u] = c[u] + r0, c[u] + r1
    lo[w], hi[w] = c[w] - width / 2, c[w] + width / 2
    el = box(lo, hi, name, part, tex)
    if abs(ang) > 1e-12:
        rotate([el], axis, math.degrees(ang), c)
    return el


def disc(axis, c, a0, a1, r, name, part, tex, k=4, phase=0.0):
    """A plain round part: k strips as long as the 2k-gon is across, 180/k degrees apart, each a hair
    shorter than the last so no two ends share a plane."""
    a = AX[axis]
    u, w = REF[axis]
    half = r * math.tan(math.pi / (2 * k))
    out = []
    st = min(0.012, (a1 - a0) / (4 * k))
    for i in range(k):
        lo, hi = [0.0] * 3, [0.0] * 3
        lo[a], hi[a] = a0 + st * i, a1 - st * i
        lo[u], hi[u] = c[u] - r, c[u] + r
        lo[w], hi[w] = c[w] - half, c[w] + half
        el = box(lo, hi, f"{name}_{i + 1}" if k > 1 else name, part, tex)
        ang = phase + math.pi * i / k
        if abs(ang) > 1e-12:
            rotate([el], axis, math.degrees(ang), c)
        out.append(el)
    return out


def annulus(axis, c, a0, a1, r_in, r_out, n, name, part, tex, phase=0.0):
    """A ring of n boxes from r_in to r_out, each as wide as a side of the n-gon at r_out; every other
    one a hair shorter along the axis, so their ends never share a plane."""
    w = 2 * r_out * math.tan(math.pi / n)
    out = []
    for i in range(n):
        s = 0.02 if i % 2 else 0.0
        out.append(radial(axis, c, a0 + s, a1 - s, r_in, r_out, w, phase + TAU * i / n, f"{name}{i + 1}", part, tex))
    return out


def gear(axis, c, a0, a1, pitch_r, n, module, name, part, tex, phase=0.0, body_k=4):
    """A spur wheel: a polygon body to the root and n teeth of `module` (phase: angle of tooth 1)."""
    width = math.pi * module / 2
    root, tip = pitch_r - 1.25 * module, pitch_r + module
    body = disc(axis, c, a0, a1, root, f"{name}_body", part, tex, k=body_k, phase=phase)
    return body + [radial(axis, c, a0 + 0.03, a1 - 0.03, root - 0.25, tip, width, phase + TAU * i / n, f"{name}_tooth{i + 1}", part, tex)
                   for i in range(n)]


def pulley(axis, c, a0, a1, r, name, part, tex="steel", hub_r=1.3):
    """A plain pulley: a rim (a ring of 12) and a web to a hub. No teeth: it drives a belt."""
    out = annulus(axis, c, a0, a1, r - 0.7, r, 12, f"{name}_rim", part, tex)
    mid = (a0 + a1) / 2
    out += disc(axis, c, mid - 0.3, mid + 0.3, r - 0.65, f"{name}_web", part, tex, k=6)
    out += disc(axis, c, a0 + 0.1, a1 - 0.1, hub_r, f"{name}_hub", part, tex)
    return out


def strut(a, b, w, d, name, part, tex, axis="x"):
    """A bar from point a to point b in the plane normal to `axis`, `w` wide in that plane, `d` along it."""
    ia = AX[axis]
    u, v = [(1, 2), (2, 0), (0, 1)][ia]
    du, dv = b[u] - a[u], b[v] - a[v]
    length = math.hypot(du, dv)
    mid = [(a[i] + b[i]) / 2 for i in range(3)]
    lo, hi = list(mid), list(mid)
    lo[u], hi[u] = mid[u] - length / 2, mid[u] + length / 2
    lo[v], hi[v] = mid[v] - w / 2, mid[v] + w / 2
    lo[ia], hi[ia] = mid[ia] - d / 2, mid[ia] + d / 2
    el = box(lo, hi, name, part, tex)
    rotate([el], axis, math.degrees(math.atan2(dv, du)), mid)
    return el


def sloped(axis, a0, a1, y0, y1, pieces, part):
    """Boxes laid on a straight slope along `axis` ("x" or "z") from a0 (height y0) to a1 (height y1),
    either way along the axis. Each piece is (s0, s1, h0, h1, c0, c1, name, tex): s along the slope from
    the a0 end (0 .. its length), h above the slope's line, c across the axis (z for x, x for z). They are
    built level from a0 and turned about the a0 end's line, so a0's end stays where it is."""
    a = AX[axis]
    acr = 2 if axis == "x" else 0
    length = a1 - a0
    dirn = 1.0 if length >= 0 else -1.0
    out = []
    for s0, s1, h0, h1, c0, c1, name, tex in pieces:
        lo, hi = [0.0] * 3, [0.0] * 3
        lo[a], hi[a] = sorted((a0 + dirn * s0, a0 + dirn * s1))
        lo[1], hi[1] = y0 + h0, y0 + h1
        lo[acr], hi[acr] = c0, c1
        out.append(box(lo, hi, name, part, tex))
    target = math.atan2(y1 - y0, abs(length))
    if abs(target) > 1e-12:
        if axis == "x":
            rotate(out, "z", math.degrees(dirn * target), (a0, y0, 0.0))
        else:
            rotate(out, "x", -math.degrees(dirn * target), (0.0, y0, a0))
    return out


def slope_len(a0, a1, y0, y1):
    return math.hypot(a1 - a0, y1 - y0)


def along(a0, a1, y0, y1, at):
    """The distance along a slope from its a0 end to the point over `at`."""
    return abs(at - a0) * slope_len(a0, a1, y0, y1) / abs(a1 - a0)


def floor_y(y0, y1, a0, a1, at):
    """A trough's floor top at `at` along its axis (its floor's top runs straight from y0 at a0 to y1 at a1)."""
    return y0 + (y1 - y0) * (at - a0) / (a1 - a0)


def under_slope(y0, y1, a0, a1, lo, hi, t):
    """The lowest point of a sloped board's underside over lo..hi along its axis (its top runs from y0 at
    a0 to y1 at a1, it is t thick), less a hundredth: where a leg or block under it stops."""
    ang = math.atan2(y1 - y0, abs(a1 - a0))
    return min(floor_y(y0, y1, a0, a1, lo), floor_y(y0, y1, a0, a1, hi)) - t / math.cos(ang) - 0.01


def trough(axis, a0, a1, y0, y1, c0, c1, side, name, part, tex="planks", t=LAUNDER_T, ends=(True, False), extra=()):
    """An open board trough along `axis` from a0 to a1 (either way), its floor's top falling or rising
    straight from y0 at a0 to y1 at a1, its outside c0..c1 across, sides `side` high over the floor,
    boards t thick; `ends` closes the a0 and a1 ends with an end board as high as the sides."""
    run = slope_len(a0, a1, y0, y1)
    pieces = [(0.0, run, -t, 0.0, c0 + t, c1 - t, f"{name}_floor", tex),
              (0.0, run, -t, side, c0, c0 + t, f"{name}_side1", tex),
              (0.0, run, -t, side, c1 - t, c1, f"{name}_side2", tex)]
    if ends[0]:
        pieces.append((0.0, t, 0.0, side, c0 + t, c1 - t, f"{name}_end1", tex))
    if ends[1]:
        pieces.append((run - t, run, 0.0, side, c0 + t, c1 - t, f"{name}_end2", tex))
    return sloped(axis, a0, a1, y0, y1, pieces + list(extra), part)


def belt(axis, c1, r1, c2, r2, a0, a1, name, part, tex="leather", t=0.3, n_wrap=8):
    """An open belt on two pulleys of radii r1, r2 about `axis` through c1, c2 (3-vectors; the axis
    coordinate is ignored), a0..a1 along the axis: two straight runs on the outer tangents and a wrap of
    short boxes round the far side of each pulley. Drawn still: a plain belt moving along its length looks
    the same."""
    ia = AX[axis]
    u, v = [(1, 2), (2, 0), (0, 1)][ia]
    p1, p2 = (c1[u], c1[v]), (c2[u], c2[v])
    d = math.hypot(p2[0] - p1[0], p2[1] - p1[1])
    base = math.atan2(p2[1] - p1[1], p2[0] - p1[0])
    beta = math.asin((r1 - r2) / d)
    out, normals = [], {}
    for side, sgn in (("a", 1), ("b", -1)):
        n = base + sgn * (math.pi / 2 - beta)
        normals[side] = n
        q1, q2 = [0.0] * 3, [0.0] * 3
        q1[u], q1[v] = p1[0] + (r1 + t / 2) * math.cos(n), p1[1] + (r1 + t / 2) * math.sin(n)
        q2[u], q2[v] = p2[0] + (r2 + t / 2) * math.cos(n), p2[1] + (r2 + t / 2) * math.sin(n)
        q1[ia] = q2[ia] = (a0 + a1) / 2
        out.append(strut(q1, q2, t, a1 - a0, f"{name}_run{side}", part, tex, axis=axis))
    for tag, (cu, cv), r, f, to in (("1", p1, r1, normals["a"], normals["b"] + TAU), ("2", p2, r2, normals["b"], normals["a"])):
        while to < f:
            to += TAU
        span = to - f
        n = max(3, int(round(n_wrap * span / math.pi)))
        for i in range(n):
            ang = f + span * (i + 0.5) / n
            seg = span / n * (r + t / 2) * 1.04
            q1, q2 = [0.0] * 3, [0.0] * 3
            for q, s in ((q1, -1), (q2, 1)):
                tang = ang + math.pi / 2
                q[u] = cu + (r + t / 2) * math.cos(ang) + s * seg / 2 * math.cos(tang)
                q[v] = cv + (r + t / 2) * math.sin(ang) + s * seg / 2 * math.sin(tang)
                q[ia] = (a0 + a1) / 2
            out.append(strut(q1, q2, t, a1 - a0 - (0.02 if i % 2 else 0.0), f"{name}_wrap{tag}_{i + 1}", part, tex, axis=axis))
    return out


def sheave(x, c, off, name, part):
    """An eccentric sheave keyed on a shaft along x through c = (y, z): a 12-sided disc SHEAVE_R across
    its flats, its centre `off` = (dy, dz) from the shaft's, SHEAVE_W thick about x."""
    sc = (0.0, c[0] + off[0], c[1] + off[1])
    return disc("x", sc, x - SHEAVE_W / 2, x + SHEAVE_W / 2, SHEAVE_R, name, part, "steel", k=6)


def yoke(x, c, e, kind, name, part):
    """The iron yoke a sheave of throw e runs in, about the shaft through c = (y, z), at rest: `kind` "y"
    (a horizontal slot: the yoke follows the sheave up and down, its top and bottom bars on the sheave) or
    "z" (an upright slot: it follows the sheave across, its north and south bars on it). Those bars clear
    the sheave's corners by a hundredth; the other pair clears its whole swing."""
    y, z = c
    t = YOKE_T
    x0, x1 = x - SHEAVE_W / 2 - 0.15, x + SHEAVE_W / 2 + 0.15
    rc = SHEAVE_RC + 0.01
    far = e + rc + YOKE_GAP
    if kind == "y":                              # top and bottom bars on the sheave; end bars beyond its swing in z
        return [box([x0, y + rc, z - far - t], [x1, y + rc + t, z + far + t], f"{name}_top", part, "iron"),
                box([x0, y - rc - t, z - far - t], [x1, y - rc, z + far + t], f"{name}_bottom", part, "iron"),
                box([x0, y - rc, z - far - t], [x1, y + rc, z - far], f"{name}_n", part, "iron"),
                box([x0, y - rc, z + far], [x1, y + rc, z + far + t], f"{name}_s", part, "iron")]
    return [box([x0, y - far - t, z - rc - t], [x1, y + far + t, z - rc], f"{name}_n", part, "iron"),
            box([x0, y - far - t, z + rc], [x1, y + far + t, z + rc + t], f"{name}_s", part, "iron"),
            box([x0, y + far, z - rc], [x1, y + far + t, z + rc], f"{name}_top", part, "iron"),
            box([x0, y - far - t, z - rc], [x1, y - far, z + rc], f"{name}_bottom", part, "iron")]


def yoke_bottom(c, e, kind):
    """The underside of a yoke's bottom bar at rest."""
    rc = SHEAVE_RC + 0.01
    return c[0] - rc - YOKE_T if kind == "y" else c[0] - (e + rc + YOKE_GAP) - YOKE_T


# ---------------------------------------------------------------- builders: the wash house frame
def build_timbers():
    """The posts, sills, wall plates, tie beams and knee braces of an open timber frame."""
    f = "frame"
    out = []
    xs = ((0.0, POST), MID_X, (X_LEN - POST, X_LEN))
    zs = ((0.0, POST), (Z_LEN - POST, Z_LEN))
    for i, (x0, x1) in enumerate(xs):
        for j, (z0, z1) in enumerate(zs):
            out.append(box([x0, 0.0, z0], [x1, PLATE_Y[0], z1], f"fr_post_{'wme'[i]}{'ns'[j]}", f, "oak"))
    for j, (z0, z1) in enumerate(zs):
        out.append(box([POST, 0.0, z0], [MID_X[0], SILL_Y, z1], f"fr_sill_{'ns'[j]}w", f, "oak"))
        out.append(box([MID_X[1], 0.0, z0], [X_LEN - POST, SILL_Y, z1], f"fr_sill_{'ns'[j]}e", f, "oak"))
        out += beam([0.0, PLATE_Y[0], z0], [X_LEN, PLATE_Y[1], z1], f"fr_plate_{'ns'[j]}", f, "oak", seg=32.0)
    for tag, (x0, x1) in (("w", (0.0, POST)), ("m", MID_X), ("e", (X_LEN - POST, X_LEN))):
        out += beam([x0, PLATE_Y[0], POST], [x1, PLATE_Y[1], Z_LEN - POST], f"fr_tie_{tag}", f, "oak", seg=24.0)
    b = BRACE
    yb = PLATE_Y[0] - b

    def brace(a, c, axis, name):
        out.append(strut(a, c, BRACE_W, POST - 0.6, name, f, "oak", axis=axis))
    # the north wall's east two (the feed box and the inlet are west); none in the south wall, the side the
    # vanner and its drive are seen from
    zc = POST / 2
    brace([MID_X[1] - 0.7, yb, zc], [MID_X[1] + b - 0.7, PLATE_Y[0] + 0.7, zc], "z", "fr_brace_nme")
    brace([X_LEN - POST + 0.7, yb, zc], [X_LEN - POST - b + 0.7, PLATE_Y[0] + 0.7, zc], "z", "fr_brace_ne")
    # the end walls: each corner post to its end tie beam
    for i, (x0, x1) in enumerate(((0.0, POST), (X_LEN - POST, X_LEN))):
        xc = (x0 + x1) / 2
        brace([xc, yb, POST - 0.7], [xc, PLATE_Y[0] + 0.7, POST + b - 0.7], "x", f"fr_brace_{'we'[i]}n")
        brace([xc, yb, Z_LEN - POST + 0.7], [xc, PLATE_Y[0] + 0.7, Z_LEN - POST - b + 0.7], "x", f"fr_brace_{'we'[i]}s")
    return out


def build_feed():
    """The feed box against the north wall, hung under the plate: its walls, floor and a spout south to
    the head of the line (the sluice's head, then the jig's sieve). A chute from outside pours in through
    the north face over its north wall; at the water levels the player tips the ore in."""
    f = "frame"
    x0, x1 = FB_X
    z0, z1 = FB_Z
    y0, y1 = FB_Y
    t = 0.8
    out = [box([x0, y0, z0], [x1, y0 + t, z1], "fr_feed_floor", f, "planks"),
           box([x0, y0 + t, z0], [x1, y1 - 1.5, z0 + t], "fr_feed_n", f, "planks"),      # low: the chute comes in over it
           box([x0, y0 + t, z1 - t], [x1, y1, z1], "fr_feed_s", f, "planks"),
           box([x0, y0 + t, z0 + t], [x0 + t, y1, z1 - t], "fr_feed_w", f, "planks"),
           box([x1 - t, y0 + t, z0 + t], [x1, y1, z1 - t], "fr_feed_e", f, "planks")]
    # the hangers: iron straps from the north plate's underside down the box's ends
    for i, x in enumerate((x0 - 0.6, x1), 1):
        out.append(box([x, y0, z0], [x + 0.6, PLATE_Y[0], z0 + 2.4], f"fr_feed_strap{i}", f, "iron"))
    # the spout: a short trough from a hole in the south wall
    sx0, sx1 = FS_X
    sz0, sz1 = FS_Z
    out += [box([sx0 + 0.6, FS_Y, sz0], [sx1 - 0.6, FS_Y + 0.6, sz1], "fr_feed_spout_floor", f, "planks"),
            box([sx0, FS_Y, sz0], [sx0 + 0.6, FS_Y + 1.6, sz1], "fr_feed_spout_w", f, "planks"),
            box([sx1 - 0.6, FS_Y, sz0], [sx1, FS_Y + 1.6, sz1], "fr_feed_spout_e", f, "planks")]
    return out


def build_water():
    """The water line's supports (the line itself is the pipes' parts): an iron strap from the middle tie
    beam down to the cross line, an oak post under the vanner's branch where it runs south, and an iron
    hanger from the south plate to its capped end."""
    f = "frame"
    y, z = WATER_IN[1], TEE_Z
    top = y + PIPE_HALF
    out = [box([31.2, top, z - 0.8], [32.8, PLATE_Y[0], z + 0.8], "fr_pipe_strap", f, "iron")]
    vy = VAN_Y - PIPE_HALF
    out.append(box([VAN_X - 0.75, 0.0, 21.0], [VAN_X + 0.75, vy, 22.5], "fr_pipe_post", f, "oak"))
    zh = VAN_Z1 - 1.6
    out.append(box([VAN_X - 0.4, VAN_Y + PIPE_HALF, zh - 0.4], [VAN_X + 0.4, PLATE_Y[0] - 0.8, zh + 0.4], "fr_pipe_hangrod", f, "iron"))
    out.append(box([VAN_X - 0.6, PLATE_Y[0] - 0.8, zh - 0.6], [VAN_X + 0.6, PLATE_Y[0], Z_LEN - POST + 0.6], "fr_pipe_hanger", f, "iron"))
    return out


def ln_floor(x):
    return floor_y(LN_Y[0], LN_Y[1], LN_X[0], LN_X[1], x)


def lsl_floor(x):
    return floor_y(LS_LAUNDER_Y[0], LS_LAUNDER_Y[1], LS_LAUNDER_X[0], LS_LAUNDER_X[1], x)


def cl_floor(x):
    return floor_y(CL_Y[0], CL_Y[1], CL_X[0], CL_X[1], x)


def pt_floor(z):
    return floor_y(PT_Y[0], PT_Y[1], PT_Z[0] + PT_T + 0.2, PT_Z[1] - PT_T - 0.2, z)


def build_launders():
    """Launder N under the head of the line (the sluice's tail, the jig's weir), launder S under the
    vanner (the table's tailings, then the vanner's), both falling east into the tail box; the tail box
    across the east end with its spigot through the east wall; the concentrate launder along the middle,
    falling west to the plate table, whose bed falls south to its outlet through the west wall. Each on
    oak blocks."""
    f = "frame"
    out = trough("x", LN_X[0], LN_X[1], LN_Y[0], LN_Y[1], LN_Z[0], LN_Z[1], 2.0, "fr_ln", f, ends=(True, False), t=0.8)
    for i, x in enumerate((24.0, 34.0, 46.0), 1):
        out.append(box([x - 1.0, 0.0, LN_Z[0] + 1.2], [x + 1.0, under_slope(LN_Y[0], LN_Y[1], *LN_X, x - 1.0, x + 1.0, 0.8),
                                                       LN_Z[1] - 1.2], f"fr_ln_block{i}", f, "oak"))
    out += trough("x", LS_LAUNDER_X[0], LS_LAUNDER_X[1], LS_LAUNDER_Y[0], LS_LAUNDER_Y[1], *LS_LAUNDER_Z, 1.6, "fr_lsl", f,
                  ends=(True, False))
    # the tail box: a floor, walls with a notch for each launder, the spigot through the east wall
    x0, x1 = TB_X
    z0, z1 = TB_Z
    t = 1.0
    out.append(box([x0, 0.0, z0], [x1, TB_FLOOR, z1], "fr_tb_floor", f, "planks"))
    out.append(box([x0, TB_FLOOR, z0], [x1, TB_Y, z0 + t], "fr_tb_n", f, "planks"))
    out.append(box([x0, TB_FLOOR, z1 - t], [x1, TB_Y, z1], "fr_tb_s", f, "planks"))
    wz0, wz1 = OVERFLOW_Z
    out.append(box([x1 - t, TB_FLOOR, z0 + t], [x1, TB_Y, wz0], "fr_tb_e1", f, "planks"))
    out.append(box([x1 - t, TB_FLOOR, wz0], [x1, OVERFLOW_Y, wz1], "fr_tb_weir", f, "planks"))
    out.append(box([x1 - t, TB_FLOOR, wz1], [x1, TB_Y, z1 - t], "fr_tb_e2", f, "planks"))
    out.append(box([x1, OVERFLOW_Y - 0.5, wz0], [X_LEN - 0.1, OVERFLOW_Y, wz1], "fr_tb_lip", f, "planks"))
    cuts = sorted([(LN_Z[0], LN_Z[1], ln_floor(x0) - 0.8), (LS_LAUNDER_Z[0], LS_LAUNDER_Z[1], lsl_floor(x0) - LAUNDER_T)])
    zz = z0 + t
    for i, (c0, c1, ytop) in enumerate(cuts, 1):
        out.append(box([x0, TB_FLOOR, zz], [x0 + t, TB_Y, c0], f"fr_tb_w{i}", f, "planks"))
        out.append(box([x0, TB_FLOOR, c0], [x0 + t, ytop - 0.02, c1], f"fr_tb_sill{i}", f, "planks"))
        zz = c1
    out.append(box([x0, TB_FLOOR, zz], [x0 + t, TB_Y, z1 - t], "fr_tb_w3", f, "planks"))
    oz0, oz1 = TAIL_OUT_Z
    oy0, oy1 = TAIL_OUT_Y
    out.append(box([x1, oy0, oz0], [X_LEN - 0.1, oy0 + 0.4, oz1], "fr_tb_spigot_floor", f, "iron"))
    out.append(box([x1, oy0 + 0.4, oz0], [X_LEN - 0.1, oy1, oz0 + 0.4], "fr_tb_spigot_n", f, "iron"))
    out.append(box([x1, oy0 + 0.4, oz1 - 0.4], [X_LEN - 0.1, oy1, oz1], "fr_tb_spigot_s", f, "iron"))
    # the concentrate launder: its west end open over the plate table's head
    out += trough("x", CL_X[1], CL_X[0], CL_Y[1], CL_Y[0], CL_Z[0], CL_Z[1], CL_SIDE, "fr_cl", f, ends=(True, False))
    for i, x in enumerate((20.0, 30.0, 42.0), 1):
        out.append(box([x - 1.0, 0.0, CL_Z[0] + 0.8], [x + 1.0, under_slope(CL_Y[1], CL_Y[0], CL_X[1], CL_X[0], x - 1.0, x + 1.0, LAUNDER_T),
                                                       CL_Z[1] - 0.8], f"fr_cl_block{i}", f, "oak"))
    out += build_plate_table()
    return out


def build_plate_table():
    """The plate table's bed: a plank floor falling south from under the concentrate launder's open end,
    its sides, a low head board and the foot board; the west side stops at the notch, from which a spout
    runs out through the west wall: the concentrate's way out at every level. Two oak blocks under it. Bare,
    it carries the concentrate out; the amalgamation plates lie in it when fitted."""
    f = "frame"
    x0, x1 = PT_X
    z0, z1 = PT_Z
    t = PT_T
    a, b = z0 + t + 0.2, z1 - t - 0.2                # the floor's run between the end boards
    ya, yb = PT_Y
    run = slope_len(a, b, ya, yb)
    sn0 = along(a, b, ya, yb, PT_NOTCH_Z[0])
    out = sloped("z", a, b, ya, yb, [
        (-0.2 - t, run + 0.2 + t, -t, 0.0, x0 + t, x1 - t, "fr_pt_floor", "planks"),
        (-0.2 - t, run + 0.2 + t, -t, PT_SIDE, x1 - t, x1, "fr_pt_side_e", "planks"),
        (-0.2 - t, sn0, -t, PT_SIDE, x0, x0 + t, "fr_pt_side_w", "planks"),
    ], f)
    # the head board (low: the launder's stream and the spigot's fall over it) and the foot board
    out.append(box([x0 + t, ya - t - 0.02 - (t + 0.2) / 8, z0], [x1 - t, ya + 0.4, z0 + t], "fr_pt_head", f, "planks"))
    out.append(box([x0, 0.0, z1 - t], [x1, yb + PT_SIDE, z1], "fr_pt_foot", f, "planks"))
    # the outlet: a spout from the notch out through the west wall
    yn = pt_floor(sum(PT_NOTCH_Z) / 2)
    out.append(box([0.1, yn - 0.6, PT_NOTCH_Z[0]], [x0 + t, yn - 0.05, PT_NOTCH_Z[1]], "fr_pt_spout", f, "planks"))
    for i, z in enumerate((31.0, 39.0), 1):
        y = under_slope(ya, yb, a, b, z - 1.0, z + 1.0, t)
        out.append(box([x0 + 1.5, 0.0, z - 1.0], [x1 - 1.5, y, z + 1.0], f"fr_pt_block{i}", f, "oak"))
    return out


def build_frame():
    return build_timbers() + build_feed() + build_water() + build_launders()


# ---------------------------------------------------------------- builders: level 1, the sluice
def sl_floor(x):
    return SL_HEAD_Y - (x - SL_X[0]) * SL_SLOPE


def build_sluice():
    """The riffled sluice: a plank box falling east from under the feed box's spout, its riffles across
    the floor, its tail open over launder N; on two trestles."""
    p = "sluice"
    x0, x1 = SL_X
    c0, c1 = SL_Z
    y0, y1 = sl_floor(x0), sl_floor(x1)
    run = slope_len(x0, x1, y0, y1)
    pieces = [(0.0, run, -1.0, 0.0, c0 + 1.0, c1 - 1.0, "sl_floor", "planks"),
              (0.0, run, -1.0, SL_SIDE, c0, c0 + 1.0, "sl_side_n", "planks"),
              (0.0, run, -1.0, SL_SIDE, c1 - 1.0, c1, "sl_side_s", "planks"),
              (0.0, 1.0, 0.0, SL_SIDE, c0 + 1.0, c1 - 1.0, "sl_head", "planks")]
    for i, xr in enumerate(RIFFLE_XS, 1):
        s = along(x0, x1, y0, y1, xr)
        pieces.append((s - RIFFLE_T, s, 0.0, RIFFLE_H, c0 + 1.0, c1 - 1.0, f"sl_riffle{i}", "oak"))
    out = sloped("x", x0, x1, y0, y1, pieces, p)
    for i, xt in enumerate(SL_TRESTLES, 1):
        under = under_slope(y0, y1, x0, x1, xt - 0.8, xt + 0.8, 1.0)
        out.append(box([xt - 0.8, under - 1.4, c0 - 1.9], [xt + 0.8, under, c1 + 1.9], f"sl_trestle{i}_beam", p, "oak"))
        for j, (za, zb) in enumerate(((c0 - 1.9, c0 - 0.3), (c1 + 0.3, c1 + 1.9)), 1):
            out.append(box([xt - 0.8, 0.0, za], [xt + 0.8, under - 1.4, zb], f"sl_trestle{i}_leg{j}", p, "oak"))
    return out


# ---------------------------------------------------------------- builders: level 2, the line shaft and the jig
LS_C = (0.0, LS[0], LS[1])


def build_lineshaft():
    """The line shaft: the axle's oak cross where it comes in at the power face, a coupling, then a steel
    shaft the length of the house, with collars either side of the middle bearing; and its three bearings,
    each hung by an iron strap from a tie beam."""
    p = "lineshaft"
    y, z = LS
    out = [box([0.0, y - 2.0, z - 1.0], [ENTRY_X - 0.01, y + 2.0, z + 1.0], "lineshaft_entrya", p, "oak"),
           box([0.01, y - 1.0, z - 2.0], [ENTRY_X, y + 1.0, z + 2.0], "lineshaft_entryb", p, "oak")]
    out += disc("x", LS_C, ENTRY_X - 0.1, HANGERS[0][0] - 0.02, 1.6, "lineshaft_coupling", p, "steel", k=6)
    out += disc("x", LS_C, LS_X[0], LS_X[1], LS_R, "lineshaft_rod", p, "steel")
    for i, x0 in enumerate((HANGERS[1][0] - 0.6, HANGERS[1][1]), 1):
        out += disc("x", LS_C, x0, x0 + 0.6, 1.3, f"lineshaft_collar{i}", p, "steel", k=6)
    h = "linehangers"
    for i, (x0, x1) in enumerate(HANGERS, 1):
        out.append(box([x0, y - BEARING_H, z - BEARING_H], [x1, y + BEARING_H, z + BEARING_H], f"ls_bearing{i}", h, "iron"))
    # the straps: the end ones against their tie beams' inner faces, the middle one under its beam
    out.append(box([POST, y + BEARING_H, z - 0.8], [POST + 0.8, PLATE_Y[0] + 1.5, z + 0.8], "ls_strap1", h, "iron"))
    out.append(box([31.2, y + BEARING_H, z - 0.8], [32.8, PLATE_Y[0], z + 0.8], "ls_strap2", h, "iron"))
    out.append(box([X_LEN - POST - 0.8, y + BEARING_H, z - 0.8], [X_LEN - POST, PLATE_Y[0] + 1.5, z + 0.8], "ls_strap3", h, "iron"))
    return out


def build_jig_moving():
    """The jig's eccentric sheave on the line shaft; the plunger: a board, a boss, its rod up through the
    guide to the yoke the sheave runs in (a horizontal slot: the plunger follows the sheave up and down)."""
    out = sheave(JIG_X, LS, (0.0, -JIG_E), "jigsheave", "jigsheave")
    p = "jigplunger"
    out += yoke(JIG_X, LS, JIG_E, "y", f"{p}_yoke", p)
    x0, x1 = PLUNGER_X
    z0, z1 = PLUNGER_Z
    y0, y1 = PLUNGER_Y
    xc = JIG_X
    zr = PLUNGER_ROD_Z
    out.append(box([x0, y0, z0], [x1, y1, z1], f"{p}_board", p, "oak"))
    out.append(box([xc - 1.2, y1, zr - 1.2], [xc + 1.2, y1 + 0.6, zr + 1.2], f"{p}_boss", p, "iron"))
    out.append(box([xc - 0.6, y1 + 0.6, zr - 0.6], [xc + 0.6, yoke_bottom(LS, JIG_E, "y") + 0.3, zr + 0.6], f"{p}_rod", p, "steel"))
    return out


def build_jig():
    """The hutch on three sills: plank walls, the partition (open under it), the sieve on its ledges, the
    weir in the east wall over the sieve and its spout east to the table's feed box, the guide across the
    plunger compartment, and the concentrate spigot out of the south wall into the concentrate launder."""
    p = "jig"
    out = []
    x0, x1 = JG_X
    z0, z1 = JG_Z
    for i, (za, zb) in enumerate(((z0 + 0.5, z0 + 2.5), (15.6, 17.6), (z1 - 2.5, z1 - 0.5)), 1):
        out.append(box([x0 + 0.5, 0.0, za], [x1 - 0.5, JG_FLOOR - 1.0, zb], f"jg_sill{i}", p, "oak"))
    out += beam([x0, JG_FLOOR - 1.0, z0], [x1, JG_FLOOR, z1], "jg_floor", p, "planks")
    out += beam([x0, JG_FLOOR, z0], [x1, JG_TOP, z0 + 1.0], "jg_wall_n", p, "planks")
    out += beam([x0, JG_FLOOR, z1 - 1.0], [x1, JG_TOP, z1], "jg_wall_s", p, "planks")
    out += beam([x0, JG_FLOOR, z0 + 1.0], [x0 + 1.0, JG_TOP, z1 - 1.0], "jg_wall_w", p, "planks")
    wz0, wz1 = WEIR_Z
    out.append(box([x1 - 1.0, JG_FLOOR, z0 + 1.0], [x1, JG_TOP, wz0], "jg_wall_e1", p, "planks"))
    out.append(box([x1 - 1.0, JG_FLOOR, wz0], [x1, WEIR_Y, wz1], "jg_weir", p, "planks"))
    out += beam([x1 - 1.0, JG_FLOOR, wz1], [x1, JG_TOP, z1 - 1.0], "jg_wall_e2", p, "planks")
    pz0, pz1 = JG_PART_Z
    out.append(box([x0 + 1.0, JG_PART_Y0, pz0], [x1 - 1.0, JG_TOP, pz1], "jg_partition", p, "planks"))
    out.append(box([x0 + 1.0, SIEVE_Y - 0.5, z0 + 1.0], [x1 - 1.0, SIEVE_Y, pz0], "jg_sieve", p, "screen"))
    out.append(box([x0 + 1.0, SIEVE_Y - 1.2, z0 + 1.0], [x1 - 1.0, SIEVE_Y - 0.5, z0 + 1.7], "jg_ledge_n", p, "oak"))
    out.append(box([x0 + 1.0, SIEVE_Y - 1.2, pz0 - 0.7], [x1 - 1.0, SIEVE_Y - 0.5, pz0], "jg_ledge_s", p, "oak"))
    # the weir's spout: a short trough east over the table's feed box
    out += [box([x1, WEIR_Y - 0.6, wz0 - 0.8], [SPOUT_X1, WEIR_Y, wz1 + 0.8], "jg_spout_floor", p, "planks"),
            box([x1, WEIR_Y, wz0 - 0.8], [SPOUT_X1, WEIR_Y + 1.2, wz0], "jg_spout_n", p, "planks"),
            box([x1, WEIR_Y, wz1], [SPOUT_X1, WEIR_Y + 1.2, wz1 + 0.8], "jg_spout_s", p, "planks")]
    # the plunger rod's guide: two bars across the plunger compartment, an iron eye between them
    zr = PLUNGER_ROD_Z
    gy0, gy1 = GUIDE_Y
    out.append(box([x0 + 1.0, gy0, zr - 2.4], [x1 - 1.0, gy1, zr - 1.2], "jg_guide_n", p, "oak"))
    out.append(box([x0 + 1.0, gy0, zr + 1.2], [x1 - 1.0, gy1, zr + 2.4], "jg_guide_s", p, "oak"))
    out.append(box([JIG_X - 1.4, gy0 + 0.1, zr - 1.2], [JIG_X - 0.62, gy1 - 0.1, zr + 1.2], "jg_guide_eye_w", p, "iron"))
    out.append(box([JIG_X + 0.62, gy0 + 0.1, zr - 1.2], [JIG_X + 1.4, gy1 - 0.1, zr + 1.2], "jg_guide_eye_e", p, "iron"))
    # the spigot: a short pipe out of the south wall with a plug, pouring into the concentrate launder
    sx0, sx1 = SPIGOT_X
    sy0, sy1 = SPIGOT_Y
    out.append(box([sx0, sy0, z1], [sx1, sy1, CL_Z[0] + 1.6], "jg_spigot", p, "iron"))
    out.append(box([sx0 - 0.3, sy1, z1 + 0.4], [sx1 + 0.3, sy1 + 0.6, z1 + 1.2], "jg_spigot_plug", p, "iron"))
    return out


# ---------------------------------------------------------------- builders: level 3, the shaking table
TILT_O = (DECK_X[0], DECK_TOP, 0.0)          # the deck is built level with its top at DECK_TOP, then turned about its west edge


def deck_y(x):
    """The deck's top at x (it falls east by DECK_TILT)."""
    return DECK_TOP - (x - DECK_X[0]) * math.tan(DECK_TILT * DEG)


def tilt(els):
    return rotate(els, "z", -DECK_TILT, TILT_O)


def build_table_moving():
    """The table's sheave on the line shaft; the deck: boards on two joists, the riffles (the east ones
    shorter, ending on a diagonal), the feed box at the north-west corner under the jig's spout, the water
    launder along the upper (west) edge, the lip over the concentrate launder; and the yoke the sheave
    runs in (an upright slot: the deck follows the sheave across, along its length), with the drop bar and
    the arm under the deck to the west joist. All one part: the deck slides."""
    out = sheave(TABLE_X, LS, (TABLE_E, 0.0), "tablesheave", "tablesheave")
    d = "tabledeck"
    dk = []
    x0, x1 = DECK_X
    z0, z1 = DECK_Z
    top = DECK_TOP
    dk += beam([x0, top - DECK_T, z0], [x1, top, z1], f"{d}_board", d, "planks")
    dk.append(box([x0 + 0.5, top - DECK_T, z1], [x1 - 0.5, top - 0.4, DECK_LIP[1]], f"{d}_lip", d, "planks"))
    for i, (jx0, jx1) in enumerate(JOISTS_X, 1):
        dk += beam([jx0, top - DECK_T - JOIST_H, z0 + 0.2], [jx1, top - DECK_T, z1 - 0.2], f"{d}_joist{i}", d, "oak")
    for i, (xr, zend) in enumerate(RIFFLES, 1):
        dk.append(box([xr - 0.5, top, WL_Z[0]], [xr + 0.5, top + DECK_RIFFLE_H, zend], f"{d}_riffle{i}", d, "oak"))
    t = 0.8
    fz0, fz1 = FEEDBOX_Z
    h = FEEDBOX_H
    dk += [box([x0, top, fz0], [FEEDBOX_X1, top + h, fz0 + t], f"{d}_fb_n", d, "planks"),
           box([x0, top, fz1 - t], [FEEDBOX_X1, top + h, fz1], f"{d}_fb_s", d, "planks"),
           box([x0, top, fz0 + t], [x0 + t, top + h, fz1 - t], f"{d}_fb_w", d, "planks"),
           box([FEEDBOX_X1 - t, top, fz0 + t], [FEEDBOX_X1, top + 0.5, fz1 - t], f"{d}_fb_e", d, "planks")]
    wz0, wz1 = WL_Z
    dk += [box([x0 + t, top, wz0], [WL_X1 - t, top + 0.5, wz1], f"{d}_wl_floor", d, "planks"),
           box([x0, top, wz0], [x0 + t, top + 1.5, wz1], f"{d}_wl_w", d, "planks"),
           box([WL_X1 - t, top, wz0], [WL_X1, top + 1.0, wz1], f"{d}_wl_e", d, "planks"),
           box([x0 + t, top, wz0], [WL_X1 - t, top + 1.5, wz0 + t], f"{d}_wl_n", d, "planks"),
           box([x0 + t, top, wz1 - t], [WL_X1 - t, top + 1.5, wz1], f"{d}_wl_s", d, "planks")]
    tilt(dk)
    out += dk
    # the yoke, the drop bar down beside the deck's west edge, and the arm under the deck to the west joist
    out += yoke(TABLE_X, LS, TABLE_E, "z", f"{d}_yoke", d)
    yb = yoke_bottom(LS, TABLE_E, "z")
    zc = LS[1]
    jy = top - DECK_T - JOIST_H + 0.4
    out.append(box([TABLE_X - 0.6, jy, zc - 0.6], [TABLE_X + 0.6, yb + 0.3, zc + 0.6], f"{d}_drop", d, "iron"))
    out.append(box([TABLE_X - 0.6, jy - 1.0, zc - 0.6], [JOISTS_X[0][0] + 0.4, jy, zc + 0.6], f"{d}_arm", d, "iron"))
    return out


def joist_under(x):
    """Where a joist's tilted underside is at its edge x, less a hair: the top of the slide under it."""
    a = DECK_TILT * DEG
    return TILT_O[1] - (x - TILT_O[0]) * math.sin(a) - (DECK_T + JOIST_H) * math.cos(a) - 0.005


def tt_floor(z):
    return floor_y(TT_Y[0], TT_Y[1], TT_Z[0], TT_Z[1], z)


def build_table():
    """The fixed parts: four legs with iron slide plates under the joists, braced along; the tails launder
    along the low edge, falling south past the deck's end to its spout (P3) over launder S, or the
    vanner's feed box. Its water comes from the water line's nozzle under the tee."""
    p = "table"
    out = []
    for i, (jx0, jx1) in enumerate(JOISTS_X, 1):
        under = joist_under(jx1)
        for j, (lz0, lz1) in enumerate(LEGS_Z, 1):
            out.append(box([jx0 - 0.3, under - 0.5, lz0 - 0.5], [jx1 + 0.3, under, lz1 + 0.5], f"tb_slide{i}{j}", p, "iron"))
            out.append(box([jx0 + 0.2, 0.0, lz0], [jx1 - 0.2, under - 0.5, lz1], f"tb_leg{i}{j}", p, "oak"))
        out.append(box([jx0 + 0.4, 6.0, LEGS_Z[0][1]], [jx1 - 0.4, 7.4, LEGS_Z[1][0]], f"tb_rail{i}", p, "oak"))
    # the tails launder
    out += trough("z", TT_Z[0], TT_Z[1], TT_Y[0], TT_Y[1], TT_X[0], TT_X[1], TT_SIDE, "tb_tt", p, ends=(True, False))
    for i, (lz0, lz1) in enumerate(TT_LEGS_Z, 1):
        y = under_slope(TT_Y[0], TT_Y[1], TT_Z[0], TT_Z[1], lz0, lz1, LAUNDER_T)
        out.append(box([TT_X[0] + 0.9, 0.0, lz0], [TT_X[1] - 0.9, y, lz1], f"tb_tt_leg{i}", p, "oak"))
    return out


# ---------------------------------------------------------------- builders: level 4, the vanner
CS_C = (0.0, CS[0], CS[1])


def run_y(x):
    """The belt's upper run's underside at x (from the foot roller's top to the head roller's top)."""
    return FOOT[1] + ROLL_R + (x - FOOT[0]) * (HEAD[1] - FOOT[1]) / (HEAD[0] - FOOT[0])


def belt_loop(name, part):
    """The endless belt round the foot and head rollers (both inside the loop): the upper and lower runs
    on their tangents and a wrap of short boxes round each roller's far side. Drawn still."""
    return belt("z", (FOOT[0], FOOT[1], 0.0), ROLL_R, (HEAD[0], HEAD[1], 0.0), ROLL_R, BELT_Z[0], BELT_Z[1], name, part,
                t=BELT_T, n_wrap=7)


def build_vanner_moving():
    """The vanner's drive pulley on the line shaft; the countershaft (its pulley, two sheaves); the worm
    shaft in line beyond it (the ratchet wheel, the worm); the shaking frame: two rails, the end ties, the
    rollers' bearings, the belt and its raised edges, the yokes the sheaves run in (upright slots: the frame
    follows them across), the pawl under the ratchet; the head roller with its worm wheel; the foot roller."""
    out = pulley("x", LS_C, *VPULLEY_X, VPULLEY_R, "vannerpulley", "vannerpulley")
    c = "vannershaft"
    hx = HEAD[0]
    out += disc("x", CS_C, CS_X[0], CS_X[1], CS_R, f"{c}_rod", c, "steel")
    out += pulley("x", CS_C, *VPULLEY_X, VPULLEY_R, f"{c}_pulley", c)
    for i, xs in enumerate(CS_SHEAVES_X, 1):
        out += sheave(xs, CS, (VN_E, 0.0), f"{c}_sheave{i}", c)
    for i, (px0, px1) in enumerate(CS_PEDESTALS, 1):
        pid = c if i <= 3 else "vannerworm"
        for j, xc in enumerate((px0 - 0.5, px1), 1):
            if (i, j) in ((3, 2), (4, 1)):
                continue                              # the countershaft's end and the worm shaft's start face the ratchet
            out += disc("x", CS_C, xc, xc + 0.5, 1.15, f"{pid}_collar{i}{j}", pid, "steel", k=4)
    # the worm shaft: its rod, the ratchet wheel at its west end, and the worm over the head roller's wheel,
    # a core and four thread rings pitched so a turn moves the wheel a tooth
    w = "vannerworm"
    out += disc("x", CS_C, WS_X[0], hx - 2.3, CS_R, f"{w}_rod", w, "steel")
    out += disc("x", CS_C, hx + 2.3, WS_X[1], CS_R, f"{w}_rod2", w, "steel")
    out += gear("x", CS_C, *RATCHET_X, RATCHET_R, RATCHET_N, RATCHET_MOD, f"{w}_ratchet", w, "steel", body_k=6)
    lead = math.pi * WHEEL_MOD
    out += disc("x", CS_C, hx - 2.3, hx + 2.3, WORM_R - 1.25 * WHEEL_MOD + 0.15, f"{w}_worm_core", w, "steel", k=4)
    for i in range(4):
        xt = hx - 1.5 * lead + i * lead
        ring = disc("x", CS_C, xt - 0.35, xt + 0.35, WORM_R + WHEEL_MOD * 0.8, f"{w}_worm_thread{i + 1}", w, "steel", k=4)
        rotate(ring, "z", -7.0, (xt, CS[0], CS[1]))
        out += ring
    f = "vannerframe"
    for j, (rz0, rz1) in enumerate(RAILS_Z, 1):
        out += beam([RAIL_X[0], RAIL_Y[0], rz0], [RAIL_X[1], RAIL_Y[1], rz1], f"{f}_rail{j}_", f, "oak")
    for i, (tx0, tx1) in enumerate(((RAIL_X[0], RAIL_X[0] + 0.6), (RAIL_X[1] - 0.8, RAIL_X[1])), 1):
        out.append(box([tx0, RAIL_Y[0] + 0.5, RAILS_Z[0][1]], [tx1, RAIL_Y[1], RAILS_Z[1][0]], f"{f}_tie{i}", f, "oak"))
    for name, (rx, ry) in (("head", HEAD), ("foot", FOOT)):
        for j, (rz0, rz1) in enumerate(RAILS_Z, 1):
            out.append(box([rx - 1.0, ry - 1.0, rz0 - 0.4 if j == 1 else rz0], [rx + 1.0, ry + 1.0, rz1 + (0.4 if j == 2 and name == "foot" else 0.0)],
                           f"{f}_{name}bearing{j}", f, "iron"))
    out += belt_loop(f"{f}_belt", f)
    for j, (ez0, ez1) in enumerate(((BELT_Z[0], BELT_Z[0] + 0.8), (BELT_Z[1] - 0.8, BELT_Z[1])), 1):
        a = [FOOT[0] + 0.4, run_y(FOOT[0] + 0.4) + BELT_T + 0.35, 0.0]
        b = [HEAD[0] - 0.4, run_y(HEAD[0] - 0.4) + BELT_T + 0.35, 0.0]
        el = strut(a, b, 0.7, ez1 - ez0 - 0.02, f"{f}_edge{j}", f, "leather", axis="z")
        el.c[2] = (ez0 + ez1) / 2
        out.append(el)
    for i, xs in enumerate(CS_SHEAVES_X, 1):
        ys = yoke(xs, CS, VN_E, "z", f"{f}_yoke{i}", f)
        out += ys
        north = next(e for e in ys if e.name.endswith("_n"))
        lo, hi = north.aabb()
        out.append(box([lo[0], RAIL_Y[1], lo[2]], [hi[0], lo[1], hi[2]], f"{f}_yoke{i}_hanger", f, "iron"))
    # the pawl: an iron bracket up from the south rail, its tip under the ratchet's lowest teeth; it pushes
    # them along with the shake, a tooth a stroke, and rides back over them
    rx0, rx1 = RATCHET_X
    tip = CS[0] - RATCHET_R - RATCHET_MOD - 0.05
    out.append(box([rx0, RAIL_Y[1], RAILS_Z[1][0] + 0.2], [rx1, tip - 0.6, RAILS_Z[1][1]], f"{f}_pawl_arm", f, "iron"))
    out.append(box([rx0, tip - 0.6, RAILS_Z[1][0] + 0.2], [rx1, tip, CS[1] + 0.9], f"{f}_pawl", f, "iron"))
    hc = (HEAD[0], HEAD[1], 0.0)
    out += disc("z", hc, BELT_Z[0] - 0.2, BELT_Z[1] + 0.2, ROLL_R, "vannerhead_drum", "vannerhead", "oak", k=6)
    out += disc("z", hc, RAILS_Z[0][0] - 0.3, WHEEL_Z[0] + 0.1, 0.5, "vannerhead_journal", "vannerhead", "steel")
    out += gear("z", hc, *WHEEL_Z, WHEEL_R, WORM_N, WHEEL_MOD, "vannerhead_wheel", "vannerhead", "steel", body_k=6)
    fc = (FOOT[0], FOOT[1], 0.0)
    out += disc("z", fc, BELT_Z[0] - 0.2, BELT_Z[1] + 0.2, ROLL_R, "vannerfoot_drum", "vannerfoot", "oak", k=6)
    out += disc("z", fc, RAILS_Z[0][0] - 0.3, RAILS_Z[1][1] + 0.3, 0.5, "vannerfoot_journal", "vannerfoot", "steel")
    return out


def build_vanner():
    """The fixed parts: slide posts with iron plates under the rails; the pedestals of the countershaft and
    the worm shaft; the concentrate box under the head roller on two bearers over launder S, its spout north
    into the concentrate launder; the feed box over the belt under the table's spout, hung from the water
    line's run beside it; the drive belt. Its wash water comes from the water line's nozzles."""
    p = "vanner"
    out = []
    for i, x in enumerate(VSLIDES_X, 1):
        for j, (rz0, rz1) in enumerate(RAILS_Z, 1):
            out.append(box([x - 1.2, RAIL_Y[0] - 0.6, rz0 - 0.6], [x + 1.2, RAIL_Y[0] - 0.02, rz1 + 0.6], f"vn_slide{i}{j}", p, "iron"))
            out.append(box([x - 0.8, 0.0, rz0 + 0.2], [x + 0.8, RAIL_Y[0] - 0.6, rz1 - 0.2], f"vn_slidepost{i}{j}", p, "oak"))
    y, z = CS
    for i, (x0, x1) in enumerate(CS_PEDESTALS, 1):
        out.append(box([x0, 0.0, 43.7], [x1, y - 1.3, 45.0], f"vn_pedestal{i}", p, "oak"))
        out.append(box([x0, y - 1.3, z - 1.3], [x1, y + 1.3, z + 1.3], f"vn_bearing{i}", p, "iron"))
    # the concentrate box under the head roller, on two bearers across launder S
    tx0, tx1 = TANK_X
    tz0, tz1 = TANK_Z
    fy, wy = TANK_Y
    t = 0.8
    out.append(box([tx0, fy - t, tz0], [tx1, fy, tz1], "vn_tank_floor", p, "planks"))
    out.append(box([tx0, fy, tz1 - t], [tx1, wy, tz1], "vn_tank_s", p, "planks"))
    out.append(box([tx0, fy, tz0 + t], [tx0 + t, wy, tz1 - t], "vn_tank_w", p, "planks"))
    out.append(box([tx1 - t, fy, tz0 + t], [tx1, wy, tz1 - t], "vn_tank_e", p, "planks"))
    sx0, sx1 = TANK_SPOUT_X
    out.append(box([tx0, fy, tz0], [sx0, wy, tz0 + t], "vn_tank_n1", p, "planks"))
    out.append(box([sx1, fy, tz0], [tx1, wy, tz0 + t], "vn_tank_n2", p, "planks"))
    out.append(box([sx0, fy, tz0], [sx1, fy + 0.6, tz0 + t], "vn_tank_lip", p, "planks"))
    sy = fy + 0.6
    out += [box([sx0, sy - 0.5, CL_Z[0] + 1.6], [sx1, sy, tz0], "vn_tank_spout", p, "planks"),
            box([sx0 - 0.5, sy - 0.5, CL_Z[0] + 1.6], [sx0, sy + 0.4, tz0], "vn_tank_spout_w", p, "planks"),
            box([sx1, sy - 0.5, CL_Z[0] + 1.6], [sx1 + 0.5, sy + 0.4, tz0], "vn_tank_spout_e", p, "planks")]
    for i, (bx0, bx1) in enumerate(((tx0 + 0.6, tx0 + 2.0), (tx1 - 2.0, tx1 - 0.6)), 1):
        out.append(box([bx0, fy - t - 0.9, 34.0], [bx1, fy - t, 43.4], f"vn_bearer{i}", p, "oak"))
        out.append(box([bx0, 0.0, 34.0], [bx1, fy - t - 0.9, 35.4], f"vn_bearer{i}_postn", p, "oak"))
        out.append(box([bx0, 0.0, 42.0], [bx1, fy - t - 0.9, 43.4], f"vn_bearer{i}_posts", p, "oak"))
    # the feed box over the belt, under the table's spout, on two posts by the south wall
    fx0, fx1 = VFEED_X
    fy0, fy1 = VFEED_Y
    bz0, bz1 = BELT_Z[0] + 1.4, BELT_Z[1] - 1.4           # inside the belt's raised edges, through the shake
    out.append(box([fx0, fy0, bz0], [fx1, fy0 + 0.4, bz1], "vn_fb_floor", p, "planks"))
    out.append(box([fx1 - 0.6, fy0 + 0.4, bz0], [fx1, fy1, bz1], "vn_fb_e", p, "planks"))
    out.append(box([fx0, fy0 + 0.4, bz0], [fx1 - 0.6, fy1, bz0 + 0.6], "vn_fb_n", p, "planks"))
    out.append(box([fx0, fy0 + 0.4, bz1 - 0.6], [fx1 - 0.6, fy1, bz1], "vn_fb_s", p, "planks"))
    vx0 = VAN_X - PIPE_HALF
    out.append(box([fx1 - 0.6, fy1, 36.0], [vx0, VAN_Y - PIPE_HALF + 1.6, 39.0], "vn_fb_hanger", p, "iron"))
    out += belt("x", LS_C, VPULLEY_R, CS_C, VPULLEY_R, VPULLEY_X[0] + 0.15, VPULLEY_X[1] - 0.15, "vn_belt", p)
    return out


# ---------------------------------------------------------------- builders: the water line (soldered pipe)
def pipe_boxes():
    """The water line as UnifiedPipes' soldered pipe (solderedpipe's boxes, build-frame voxels): the inlet
    from the water face, on the axis of the ppex pipe beyond it, to a tee; west from the tee to a closed hub
    over the head of the line; east from it to an elbow, down to a second elbow under the line shaft, and
    south in two lengths, a joint at their seam, to its end over the belt, plugged with solder. A nozzle
    under the head of the line's hub, one under the tee (the table's), three along the vanner's run. As in
    the rosser's drip, the inlet has no joint at the face: the pipe beyond brings its own."""
    h = PIPE_HALF
    x, y = WATER_IN
    z = TEE_Z
    out = solderedpipe.tube("inlet", 2, 0.0, z - h, (x, y))
    out += solderedpipe.hub("tee", (x, y, z), ("north", "west", "east"))
    out += solderedpipe.tube("head", 0, HEAD_OUT_X + h, x - h, (z, y))
    out += solderedpipe.hub("headend", (HEAD_OUT_X, y, z), ("east",))
    out += solderedpipe.tube("cross", 0, x + h, VAN_X - h, (z, y))
    out += solderedpipe.hub("elbow1", (VAN_X, y, z), ("west", "down"))
    out += solderedpipe.tube("drop", 1, VAN_Y + h, y - h, (VAN_X, z))
    out += solderedpipe.hub("elbow2", (VAN_X, VAN_Y, z), ("up", "south"))
    out += solderedpipe.tube("van1", 2, z + h, VAN_SEAM, (VAN_X, VAN_Y))
    out += solderedpipe.tube("van2", 2, VAN_SEAM, VAN_Z1, (VAN_X, VAN_Y))
    out += solderedpipe.joint("joint_van", 2, VAN_SEAM, (VAN_X, VAN_Y))
    i_ = h - solderedpipe.WALL
    out.append(solderedpipe.Box("cap", [VAN_X - i_, VAN_Y - i_, VAN_Z1 - solderedpipe.WALL], [VAN_X + i_, VAN_Y + i_, VAN_Z1],
                                solderedpipe.SOLDER))
    noz = [("nozzle_head", HEAD_OUT_X, y, z), ("nozzle_table", x, y, TABLE_OUT_Z)]
    noz += [(f"nozzle_van{i}", VAN_X, VAN_Y, zn) for i, zn in enumerate(VAN_NOZZLES_Z, 1)]
    for name, nx, ny, nz in noz:
        out.append(solderedpipe.Box(name, [nx - 0.6, ny - h - NOZZLE_Y, nz - 0.6], [nx + 0.6, ny - h, nz + 0.6], solderedpipe.BODY))
    return solderedpipe.cull_pressed(out)


def build_pipes(metal_=PIPE_METALS[0]):
    """The water line as elements of the part `pipe<metal_>` (`pipe_boxes`): the body in that metal's pipe
    texture, the joints in solder, each face's UVs solderedpipe's on this model's texture scale. Built in
    copper; `pipe_copies` makes lead."""
    p = f"pipe{metal_}"
    codes = {solderedpipe.BODY: f"#{p}", solderedpipe.SOLDER: "#solder"}
    out = []
    for b in pipe_boxes():
        size = b.size()
        faces = {d: {"texture": codes[b.role], "uv": solderedpipe.uv(d, size, TEX / 16)} for d in b.faces}
        out.append(El(f"{p}_{b.name}", size, [(b.lo[k] + b.hi[k]) / 2 for k in range(3)], [row[:] for row in IDENT], faces, p))
    return out


def pipe_copies(els):
    """The water line in the other metals: the first metal's elements as they are (after the z-fighting
    fix, so every copy has the same insets), renamed and in their own metal's texture; the joints stay
    solder. Only one metal's are ever drawn, so the copies are not checked against each other."""
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


# ---------------------------------------------------------------- builders: the amalgamation plates
def build_plates():
    """The amalgamation plates (`plates`): three copper plates laid in the plate table's bed and an iron
    riffle across it after them, the trap that holds the amalgam the pulp carries off. The dressing
    (`dressing`): the mercury rubbed into each plate, a thin coat, and the mercury lying behind the trap."""
    x0, x1 = PT_X[0] + PT_T, PT_X[1] - PT_T
    a, b = PT_Z[0] + PT_T + 0.2, PT_Z[1] - PT_T - 0.2
    ya, yb = PT_Y
    s = lambda zz: along(a, b, ya, yb, zz)       # noqa: E731
    pieces = [(s(z0), s(z1), 0.0, PLATE_T, x0 + 0.2, x1 - 0.2, f"pl_plate{i + 1}", "copper") for i, (z0, z1) in enumerate(PLATE_Z)]
    pieces.append((s(TRAP_Z[0]), s(TRAP_Z[1]), 0.0, TRAP_H, x0, x1, "pl_trap", "iron"))
    out = sloped("z", a, b, ya, yb, pieces, "plates")
    coats = [(s(z0) + 0.2, s(z1) - 0.2, PLATE_T, PLATE_T + COAT_T, x0 + 0.5, x1 - 0.5, f"pd_coat{i + 1}", "mercury")
             for i, (z0, z1) in enumerate(PLATE_Z)]
    coats.append((s(PLATE_Z[-1][1]) + 0.05, s(TRAP_Z[0]) - 0.02, 0.0, 0.4, x0 + 0.05, x1 - 0.05, "pd_well", "mercury"))
    out += sloped("z", a, b, ya, yb, coats, "dressing")
    return out


def build():
    return (build_lineshaft() + build_jig_moving() + build_table_moving() + build_vanner_moving()
            + build_sluice() + build_jig() + build_table() + build_vanner() + build_plates() + build_pipes() + build_frame())


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


WORM_RATIO = RATCHET_RATIO                   # the worm shaft turns with the ratchet
HEAD_RATIO = -WORM_RATIO / WORM_N            # the head roller: its top runs east (up the belt, to the head), always
FOOT_RATIO = HEAD_RATIO                      # the same radius, the same belt


def rot(axis, c, ratio, travel=False):
    """A rotate about `axis` through c (x, y, z voxels) by ratio x theta (or x psi, the travel)."""
    d = {"type": "rotate", "axis": axis, "pivot": pt(*c), "ratio": r6(ratio)}
    if travel:
        d["input"] = "travel"
    return d


def slide(axis, amplitude):
    return {"type": "slide", "axis": axis, "amplitude": r6(amplitude / B), "ratio": 1.0}


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    ls = (0.0, *LS)
    parts = [
        # the line shaft turns with the axle; the sheaves and the vanner's pulley are keyed on it
        {"id": "lineshaft", "match": ["lineshaft_*"], "requires": "lineshaft", "drivers": [rot("x", ls, 1.0)]},
        {"id": "jigsheave", "match": ["jigsheave_*"], "requires": "jig", "ride": "lineshaft", "drivers": []},
        {"id": "tablesheave", "match": ["tablesheave_*"], "requires": "table", "ride": "lineshaft", "drivers": []},
        {"id": "vannerpulley", "match": ["vannerpulley_*"], "requires": "vanner", "ride": "lineshaft", "drivers": []},
        # each yoke follows its sheave's centre exactly: up and down (the jig's plunger), across (the deck)
        {"id": "jigplunger", "match": ["jigplunger_*"], "requires": "jig", "drivers": [slide("y", JIG_E)]},
        {"id": "tabledeck", "match": ["tabledeck_*"], "requires": "table", "drivers": [slide("z", TABLE_E)]},
        # the vanner: its countershaft belted 1:1 from the line shaft, the frame shaken by its two sheaves;
        # the worm shaft turned forward by the ratchet the pawl on the frame pushes a tooth a stroke (so by the
        # travel, either way), the head roller crept by the worm, the foot roller turned by the belt
        {"id": "vannershaft", "match": ["vannershaft_*"], "requires": "vanner", "drivers": [rot("x", (0.0, *CS), 1.0)]},
        {"id": "vannerworm", "match": ["vannerworm_*"], "requires": "vanner", "drivers": [rot("x", (0.0, *CS), WORM_RATIO, True)]},
        {"id": "vannerframe", "match": ["vannerframe_*"], "requires": "vanner", "drivers": [slide("z", VN_E)]},
        {"id": "vannerhead", "match": ["vannerhead_*"], "requires": "vanner", "ride": "vannerframe",
         "drivers": [rot("z", (HEAD[0], HEAD[1], 0.0), HEAD_RATIO, True)]},
        {"id": "vannerfoot", "match": ["vannerfoot_*"], "requires": "vanner", "ride": "vannerframe",
         "drivers": [rot("z", (FOOT[0], FOOT[1], 0.0), FOOT_RATIO, True)]},
        # the fixed parts of each level, and the frame
        {"id": "sluice", "match": ["sl_*"], "requires": "sluice", "drivers": []},
        {"id": "linehangers", "match": ["ls_*"], "requires": "lineshaft", "drivers": []},
        {"id": "jig", "match": ["jg_*"], "requires": "jig", "drivers": []},
        {"id": "table", "match": ["tb_*"], "requires": "table", "drivers": []},
        {"id": "vanner", "match": ["vn_*"], "requires": "vanner", "drivers": []},
        {"id": "plates", "match": ["pl_*"], "requires": "plates", "drivers": []},
        {"id": "dressing", "match": ["pd_*"], "requires": "dressing", "drivers": []},
        # the water line, one part per metal: only the supplied metal's is drawn
        *[{"id": pid, "match": [f"{pid}_*"], "requires": pid, "drivers": []} for pid in PIPES],
        {"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []},
    ]
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# ---------------------------------------------------------------- poses and states
REST = (0.0,)                                 # a pose is (theta,) or (theta, state): turned from 0, so the travel is |theta|


def inputs_of(pose):
    return {"theta": pose[0], "travel": abs(pose[0])}


def pm(parts, pid, pose, path=None):
    return _part_matrix(parts, pid, inputs_of(pose), path)


LEVEL_STATES = {                              # what each level fits
    "frame": (),
    "level1": ("sluice",),
    "level2": ("lineshaft", "jig"),
    "level3": ("lineshaft", "jig", "table"),
    "level4": ("lineshaft", "jig", "table", "vanner"),
}
STATES = {                                    # what is fitted in each state checked: the levels, alone and with the
    **{k: v + (PIPES[0],) for k, v in LEVEL_STATES.items()},     # plates, with the water line in copper (lead is
    "level2plates": ("lineshaft", "jig", "plates", PIPES[0]),    # the same elements, never drawn with it)
    "level3dressed": ("lineshaft", "jig", "table", "plates", "dressing", PIPES[0]),
    "level4dressed": ("lineshaft", "jig", "table", "vanner", "plates", "dressing", PIPES[0]),
}


def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


ANCHORS = ("feedSpout", "waterSpout", "concentrate", "tailings")


def make_rig(parts):
    nz = sum(PT_NOTCH_Z) / 2
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the wash house's "
                    "north-west corner, under the feed box; the line shaft runs east from the power face. A frame, and "
                    "four levels fitted into it (levels: what each fits, by requires; a level keeps the last's parts, "
                    "but level 2 takes the sluice out). Level 4's water is recirculated by the gameplay: its return leaves "
                    "at returnWaterCell's face; nothing in the machine carries it back. theta is the axle: the line shaft turns with it and everything "
                    "is driven off it. A cell's boxes are the frame's; its fitted boxes are there only while that part "
                    "is fitted. See the concentrator's README for the schema.",
        "cells": [],
        "powerCell": list(POWER_CELL),
        "powerFace": POWER_FACE,
        "waterCell": list(WATER_CELL),
        "waterFace": WATER_FACE,
        "feedCell": list(FEED_CELL),
        "feedFace": FEED_FACE,
        "feedSpout": {"pos": pt(*FEED_LIP)},
        "waterSpout": {"pos": pt(*WATER_LIP)},
        "concentrate": {"pos": pt(0.0, pt_floor(nz) - 0.05, nz)},
        "concentrateSide": "west",
        "tailings": {"pos": pt(X_LEN, TAIL_OUT_Y[0] + 0.4, sum(TAIL_OUT_Z) / 2)},
        "tailingsSide": "east",
        "returnWaterCell": list(RETURN_CELL),
        "returnWaterFace": RETURN_FACE,
        "levels": [{"level": n, "tier": tier, "name": name, "requires": list(req)} for n, tier, req, name in LEVELS],
        "pipes": {"requires": {m: f"pipe{m}" for m in PIPE_METALS},
                  "_comment": "The water line, from the inlet to its nozzles: ppex's straight pipe, soldered, one part per "
                              "metal (the same elements); only the supplied metal's is drawn."},
        "plates": {"requires": PLATES[0], "dressing": PLATES[1], "fromLevel": PLATES_FROM,
                   "_comment": "The amalgamation plates: one fitted part from fromLevel on, not a level of their own; "
                               "dressing is their mercury coat, drawn over them while they are dressed."},
        "drive": {"strokesPerTurn": {"jig": 1.0, "table": 1.0, "vanner": 1.0},
                  "beltPerTurn": r6(abs(HEAD_RATIO) * ROLL_R / B * TAU),
                  "ratchet": {"teeth": RATCHET_N, "turnsPerTurn": r6(RATCHET_RATIO)},
                  "_comment": "strokesPerTurn: each machine's working strokes (the plunger's, the deck's, the vanner's "
                              "shakes) per turn of the axle, the line shaft turning with it. beltPerTurn: "
                              "blocks the vanner's belt creeps to its head per turn of the axle, either way (the ratchet "
                              "turns the worm shaft forward a tooth a stroke; a single-start worm on a 12-tooth wheel)."},
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
    for p in ship_parts:
        for drv in p["drivers"]:
            for key in ("pivot", "anchor"):
                if key in drv:
                    drv[key] = [r6(drv[key][k] + db[k]) for k in range(3)]
    ship = copy.deepcopy(rig)
    for key in ("powerCell", "waterCell", "feedCell", "returnWaterCell"):
        ship[key] = shift_cell(rig[key], ORIGIN_CELL)
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig: `boxes` from
    the frame's elements, and `fitted`, per requires value, from the elements of the parts that need it
    (each reader rebuilds them the same way). A cell with nothing in it at any level is hollow; then the
    lids over every column."""
    written = flatten(shape["elements"], textures={})
    req = {p["id"]: p["requires"] for p in ship_parts}
    rest = []
    for w in written:
        pid = part_of(ship_parts, w.name)
        rest.append((req[pid], posed(w, _part_matrix(ship_parts, pid, inputs_of(REST)))))
    by_cell = {}
    for r, el in rest:
        lo, hi = el.aabb()
        for c in cells_touched(lo, hi):
            by_cell.setdefault(c, {}).setdefault(r, []).append(el)
    order = [None, *REQUIRES, *PLATES, *PIPES]
    out = []
    for c in footprint():
        pos = tuple(c[k] - ORIGIN_CELL[k] for k in range(3))
        groups = by_cell.get(pos, {})
        cell = {"pos": list(pos)}
        frame = cell_boxes(groups[None], pos) if None in groups else None
        if frame:
            cell["boxes"] = frame
        fitted = {}
        for r in order[1:]:
            if r in groups:
                bs = cell_boxes(groups[r], pos)
                if bs:
                    fitted[r] = bs
        if fitted:
            cell["fitted"] = fitted
        if not frame and not fitted:
            cell["hollow"] = True
        out.append(cell)
    return with_lids(out)


def check_shipped(els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    poses = [REST, (1.3,), (-2.2,), (4.9,), (9.1,)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose)), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; {len(cells)} cells")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
def reference_poses():
    """(theta, travel): turned from rest either way, and with travel run up both ways first."""
    out = []
    for th in (0.0, 0.7, -1.9, 2.6, 4.1, -5.3, 11.05):
        for extra in (0.0, 3.7):
            out.append((th, round(abs(th) + extra, 6)))
    return out


def reference_json(ship_parts):
    poses = []
    for th, ps in reference_poses():
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], {"theta": th, "travel": ps})) for q in ship_parts}
        poses.append({"theta": th, "travel": ps, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped concentrator-rig.json's parts: each part's matrix as 3 rows of 4 "
                        "(block units) at each pose (theta the axle's angle, travel its total travel either way, radians). The site's "
                        "and the mod's tests check "
                        "their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. The wash house frame (fr_*) and "
             "each level's parts (sl_: sluice; lineshaft / ls_: line shaft; jig* / jg_: jig; table* / tb_: table; "
             "vanner* / vn_: vanner; pl_ / pd_: amalgamation plates and dressing; pipecopper_ / pipelead_: the water "
             "line in each metal). Keep element "
             "names when editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


def state_of(pose):
    return pose[1] if len(pose) > 1 else "all"


def on_show(requires, state):
    """Whether a part needing `requires` shows in a state ("all": every level at once)."""
    return requires is None or state == "all" or requires in STATES[state]


def shown(posed_els, pose, req_of):
    """The posed elements as they can be seen in the pose's state: other levels' parts moved far away
    (copies; the order kept), so the z-fighting fix and its check deal only with faces that show together."""
    state = state_of(pose)
    out = []
    for e in posed_els:
        if not on_show(req_of[e.part], state):
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 - 10.0 * len(out), e.c[2]]
        out.append(e)
    return out


def coplanar_poses():
    out = []
    for state in ("level4dressed", "level1", "level2plates", "level3"):
        for th in (0.0, 0.9, 2.3, 4.0):
            out.append((th, state))
    return tuple(out)


def fix_coplanar(els, parts):
    req_of = {p["id"]: p["requires"] for p in parts}
    return fix_coplanar_posed(els, lambda es, pose: shown([posed(el, pm(parts, el.part, pose)) for el in es], pose, req_of),
                              coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the concentrator's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_concentrator
    els = build()
    for el in els:
        el.r = [[0.0 if abs(v) < 1e-9 else (math.copysign(1.0, v) if abs(abs(v) - 1) < 1e-9 else v) for v in row] for row in el.r]
    parts = rig_parts()
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        n = sum(len(v) for v in before.values())
        print(f"coplanar faces before the fix: {n} pairs over {len(before)} poses; {hidden} faces pressed against their own part removed")
    els += pipe_copies(els)
    rig = make_rig(parts)
    ok = validate_concentrator.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "concentrator.json", args.out / "concentrator_frame.json", args.out / "concentrator-rig.json",
                args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "concentrator.json", SHAPE_DIR / "concentrator_frame.json", RIG_DIR / "concentrator-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_concentrator.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
    texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship), reference_dumps(reference_json(ship_parts)))
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
