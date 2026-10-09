#!/usr/bin/env python3
"""Generate the eidolon's shape: vanilla's unused mobile eidolon, frozen into our namespace, with the
laborer's animations, attachment points and build stages (Python 3.11 stdlib only).

The body is Anomalous Games' model (Vintage Story's `entity/lore/eidolon/normal.json`, game 1.22.7;
see ../../CREDITS.md). This script reads it from a game install and writes

    assets/seraphhorizons/shapes/entity/eidolon/eidolon.json   the shape
    assets/seraphhorizons/config/eidolon-stages.json            the build stages, element by element

The vanilla file is pinned by sha256: a game update that changes it stops the script instead of
silently changing the snapshot. Elements, their geometry and the vanilla animations it keeps are
copied unchanged; textures point at the game's own files by `game:` path (none are copied), with
one restyle (`RESTYLE`, undone by --vanilla-look). Everything else is authored here: three anchor
elements, five attachment points and the laborer's animations, posed by `kin.py`'s copy of the
game's pose maths (arms reach their grips by a deterministic solver, feet are flattened and set on
the ground). See ../README.md.

    python3 mods-src/seraphhorizons/Eidolon/tools/make_shape.py            # write the files
    python3 mods-src/seraphhorizons/Eidolon/tools/make_shape.py --check    # compare, write nothing
    python3 mods-src/seraphhorizons/Eidolon/tools/make_shape.py --report   # the pose checks

The game install comes from --game or $VINTAGE_STORY.
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import kin  # noqa: E402

HERE = Path(__file__).resolve().parent
MOD = HERE.parents[1]
SHAPE_OUT = MOD / "assets" / "seraphhorizons" / "shapes" / "entity" / "eidolon" / "eidolon.json"
STAGES_OUT = MOD / "assets" / "seraphhorizons" / "config" / "eidolon-stages.json"
VANILLA_REL = Path("assets") / "survival" / "shapes" / "entity" / "lore" / "eidolon" / "normal.json"
VANILLA_SHA256 = "534f1c0810b10208b11198c04f962add636b95eccd3ea9255d0f4f72228293db"  # game 1.22.7
FPS = 30

# ---------------------------------------------------------------------------------------------
# Textures. The vanilla map has no domain, so from our namespace each would resolve to
# seraphhorizons:...; every path is made game: explicit. One restyle, so the body reads as
# player-built: the rusty iron armour (hood, plates, tassets, bracers, staples) becomes tarnished
# brass, Jonas's metal. --vanilla-look writes the original map.
VANILLA_TEXTURES = {
    "steel": "block/metal/tarnished/steel",
    "lattice": "block/machine/lattice",
    "charred": "block/wood/charred",
    "poor": "block/leather/poor",
    "chain-rust": "block/metal/tarnished/chain-rust",
    "rustyglow": "block/machine/statictranslocator/rustyglow",
    "rusty-iron": "block/metal/tarnished/rusty-iron",
    "fire-red": "block/fire-red",
    "reedrope": "block/cloth/reedrope",
}
RESTYLE = {"rusty-iron": "block/metal/tarnished/brass"}

# ---------------------------------------------------------------------------------------------
# Vanilla animations: kept unchanged, or dropped. Every vanilla code is in exactly one list.
KEEP = [
    "stand-walk", "stand-run", "stand-idle1", "stand-idle2", "stand-alert",
    "stand-punch", "stand-kick", "stand-slam", "stand-slash", "stand-throw", "stand-stagger",
    "stand-die", "toppleover", "stand-inactive", "stand-activate",
    "weapon-walk", "weapon-run", "weapon-idle1", "weapon-alert", "weapon-stab", "weapon-kick",
    "weapon-stagger", "weapon-die",
]
DROP = {
    "stand-hurteye": "the boss's eye weak point: a hand thrown over the struck eye",
    "weapon-hurteye": "the same, holding a weapon",
    "stand-eyesweep": "the boss's eye-beam sweep",
    "ground-inactive": "the legless ground state the boss falls into (toppleover); the laborer never loses its legs",
    "ground-activate": "ground state",
    "ground-idle": "ground state",
    "ground-crawl": "ground state",
    "ground-throw": "ground state",
    "ground-slash": "ground state",
    "ground-slam": "ground state",
    "ground-die": "ground state",
}

# ---------------------------------------------------------------------------------------------
# Geometry, in model voxels at entity size 1 (the model is 60 voxels, 3.75 blocks, tall). The model
# faces −x; its right side is −z; the entity's position is (8, 0, 8).
SIZE = 1.0          # the entity's render size the contact poses are made for (see README)
BLOCK = 16 / SIZE   # one world block in model voxels
CENTRE_Z = 8.0
# A carried block (Carry On) sits on the Carry point, which is the centre of its underside.
CARRY_REST = (-7.5, 31.0, CENTRE_Z)          # held in both arms in front of the chest
BLOCK_GROUND = (-9.5, 0.0, CENTRE_Z)          # where lift takes it from and setdown leaves it
BLOCK_GRIP_Y = 0.75                           # grips on its sides, this far up (of a block)
# A carried trunk sits on the Trunk point, the middle of its underside (TrunkEntities' origin).
# Thin trunks are 1×1×4 blocks, carried on the left shoulder; thick ones have their own point below.
TRUNK_WIDTH = BLOCK
TRUNK_LENGTH = 4 * BLOCK
TRUNK_REST = (6.0, 53.0, 22.0)                # on the left shoulder, front to back
TRUNK_GROUND = (-10.5, 0.0, CENTRE_Z)         # lying across in front of the feet
# Grips, in the Carry or Trunk point's frame. The Trunk frame has the trunk's length along z: on
# the shoulder z runs to the model's +x (back), and x to the model's −z (right); lying across in
# front, its axes are the model's.
BLOCK_GRIPS = {"R": ("Carry", (0.0, BLOCK_GRIP_Y * BLOCK, -(BLOCK / 2 + 0.8))),
               "L": ("Carry", (0.0, BLOCK_GRIP_Y * BLOCK, BLOCK / 2 + 0.8))}
TRUNK_TOP_GRIPS = {"R": ("Trunk", (0.0, TRUNK_WIDTH * 0.9, -7.0)), "L": ("Trunk", (0.0, TRUNK_WIDTH * 0.9, 7.0))}
TRUNK_NEAR_GRIPS = {"R": ("Trunk", (TRUNK_WIDTH * 0.55, TRUNK_WIDTH * 0.45, -7.0)),
                    "L": ("Trunk", (TRUNK_WIDTH * 0.55, TRUNK_WIDTH * 0.45, 7.0))}
TRUNK_HEAVE_GRIPS = {"R": ("Trunk", (-TRUNK_WIDTH * 0.5, 2.0, -10.0)), "L": ("Trunk", (-TRUNK_WIDTH * 0.5, 2.0, 12.0))}
SHOULDER_GRIPS = {"L": ("Trunk", (-(TRUNK_WIDTH / 2 + 0.8), TRUNK_WIDTH * 0.35, -10.0))}  # outer side, in front
# Thick trunks (2×2×5 blocks, Logging Expanded's xxl) on the ThickTrunk point: carried across the body
# in both arms, low against the belly, the palms on its near side just above the bottom edge and the
# fists curled under it. Its frame has the model's axes when the anchor is unturned: length along z,
# the near side (towards the body) at x = +width/2.
THICK_WIDTH = 2 * BLOCK
THICK_LENGTH = 5 * BLOCK
THICK_REST = (-23.0, 20.0, CENTRE_Z)          # held in front (the carry poses lean it to about (−25, 24))
THICK_GROUND = (-26.0, 0.0, CENTRE_Z)         # lying across in front of the feet, 10 voxels past the toes
THICK_RISING = (-25.5, 10.0, CENTRE_Z)        # on the way up, at the squat
THICK_RISEN = (-24.5, 17.5, CENTRE_Z)         # on the way up, standing
THICK_GRIPS = {"R": ("ThickTrunk", (THICK_WIDTH / 2 + 1.0, 3.0, -11.0)),
               "L": ("ThickTrunk", (THICK_WIDTH / 2 + 1.0, 3.0, 11.0))}
HUNG_CLEAR = 3.0  # hung in the gantry, the lowest toe this far above the floor
# Felling: the hands meet on the handle here at the moment of the cut.
FELL_IMPACT_R = (-13.0, 31.0, 7.0)
FELL_IMPACT_L = (-9.0, 29.5, 8.5)

# The elements the vanilla animations move (the joints the game builds). The new animations move
# only these and the three anchors, so the joint count stays at 39.
VANILLA_JOINTS = {
    "origin", "hip-inside", "chest-inside", "head-inside", "Eye-bracket", "Eye-out", "spine1",
    "upper-armR", "lower-armR", "wristR", "palmR", "fingerR1", "fingerR2", "fingerR3", "thumbR1", "thumbR2",
    "upperarmL", "lower-armL", "wristL", "palmL", "fingerL1", "fingerL2", "fingerL3", "thumbL1", "thumbL2",
    "upperlegR", "lowerlegR", "footR", "upperlegL", "lowerlegL", "footL",
    "chainskirt-back1", "chainskirt-back2", "chainskirt-back3", "chainskirt-front1", "chainskirt-front2",
}
ANCHORS = ("carry-anchor", "trunk-anchor", "thick-trunk-anchor")

ARM = {"R": ("upper-armR", "lower-armR", "wristR"), "L": ("upperarmL", "lower-armL", "wristL")}
LEG = {"R": ("upperlegR", "lowerlegR", "footR", ("soleR1", "soleR2")),
       "L": ("upperlegL", "lowerlegL", "footL", ("soleL1", "soleL2"))}

# ---------------------------------------------------------------------------------------------
# Build stages. Each lists the elements it adds, as claim roots: a root and everything under it
# that no other root claims. The gantry shows stages cumulatively, so an element's parent is in its
# own stage or an earlier one. Ingredients are the proposal (README); the gantry agent owns them.
STAGES = [
    {"stage": 1, "code": "gantry", "name": "Gantry",
     "ingredients": ["wood (the gantry's own model)"], "roots": []},
    {"stage": 2, "code": "pelvis", "name": "Pelvis",
     "ingredients": ["game:eidolongearbox", "game:jonasframes-gearbox02", "2 game:metalplate-steel"],
     "roots": ["origin", "hip-inside", "hip-tassetR", "hip-tassetL", "back-tassetR", "back-tassetL",
               "waist-fauld", "chainskirt-back1", "chainskirt-front1", "bar-hip"]},
    {"stage": 3, "code": "legs", "name": "Legs",
     "ingredients": ["2 game:jonasframes-joint01", "game:jonasframes-spring01", "2 game:rod-steel",
                     "2 game:metalplate-steel"],
     "roots": ["bar-legs", "upperlegR", "upperlegL"]},
    {"stage": 4, "code": "torso", "name": "Torso",
     "ingredients": ["game:eidolongearbox", "game:jonasparts-tank01", "game:jonasparts-tank02",
                     "game:jonasparts-pumphead", "2 game:metalplate-steel"],
     "roots": ["chest-inside", "collar-front", "collar-R", "collar-L", "chest-plateR", "chest-plateL",
               "chest-backplate", "chest-sideplateR", "chest-sideplateL", "chest-sash2",
               "bar-chestR1", "bar-chestR2", "bar-chestL1", "bar-chestL2", "spine1",
               "carry-anchor", "trunk-anchor", "thick-trunk-anchor"]},
    {"stage": 5, "code": "arms", "name": "Arms",
     "ingredients": ["game:eidolongearbox", "game:jonasframes-gears01", "game:jonasframes-gears02",
                     "game:jonasparts-cylinder01", "game:jonasparts-valve01", "2 game:rod-steel",
                     "game:metalplate-steel"],
     "roots": ["bar-arms", "upper-armR", "upperarmL"]},
    {"stage": 6, "code": "head", "name": "Head",
     "ingredients": ["game:jonasframes-oscillator01", "game:jonasparts-cylinder02",
                     "game:jonasframes-gearbox01", "game:jonasparts-connector01", "game:metalplate-steel"],
     "roots": ["neck", "head-inside", "hood-back3", "hood-back4"]},
    {"stage": 7, "code": "mind", "name": "Mind",
     "ingredients": ["game:rustypart-eidolon2tr", "game:gear-temporal"],
     "roots": ["brain", "heart", "Eye-out"]},
]


# ---------------------------------------------------------------------------------------------
# Pose helpers. A pose is {element: {"rot"|"off": (x, y, z)}}; unset is the rest pose.

def r(x=0.0, y=0.0, z=0.0):
    return {"rot": (float(x), float(y), float(z))}


def ro(rot=(0, 0, 0), off=(0, 0, 0)):
    return {"rot": tuple(map(float, rot)), "off": tuple(map(float, off))}


def merge(*poses):
    out = {}
    for p in poses:
        for n, k in p.items():
            out.setdefault(n, {}).update(copy.deepcopy(k))
    return out


def scaled(pose, f):
    return {n: {g: tuple(v * f for v in vs) for g, vs in k.items()} for n, k in pose.items()}


# The vanilla curled hands (stand-inactive's), as a grip.
FIST = {"R": {"palmR": r(67.5), "fingerR1": r(67.5), "fingerR2": r(67.5), "fingerR3": r(67.5),
              "thumbR1": r(45), "thumbR2": r(0, -22.5)},
        "L": {"palmL": r(-67.5), "fingerL1": r(-67.5), "fingerL2": r(-67.5), "fingerL3": r(-67.5),
              "thumbL1": r(-45), "thumbL2": r(0, 22.5)}}
LIMP = {s: scaled(p, 0.45) for s, p in FIST.items()}
HAND_ELEMENTS = {s: set(p) for s, p in FIST.items()}


class Key:
    """One authored key: a base pose and what to solve for it."""

    def __init__(self, frame, pose=None, plant=None, grips=None, anchors=None, hint=None, clear=0.0):
        self.frame = frame
        self.clear = clear            # with plant: the lowest point this far above the ground instead
        self.pose = merge(pose or {})
        self.plant = plant            # None; "feet": flatten the feet, lowest sole on the ground; "sole": the
        #                               lowest sole on the ground, feet as posed; "legs": the lowest leg part
        self.grips = grips or {}      # {"R"|"L": ("world", xyz) | (anchor, local xyz)}
        self.anchors = anchors or {}  # {anchor: ("rest",) | ("world", xyz, yaw)}
        self.hint = hint or {}        # {"R"|"L": (ux, uy, uz, elbow)} start and preferred arm angles


class Builder:
    def __init__(self, shape):
        self.shape = shape
        self.rig = kin.Rig(shape)
        self.ap = {}
        for n in self.rig.order:
            for a in self.rig.elements[n].get("attachmentpoints", []):
                self.ap[a["code"]] = (n, a)
        self._rot_cache = {}

    # -- solving ------------------------------------------------------------------------------
    def grip_point(self, side, pose):
        n, a = self.ap["RightHand" if side == "R" else "LeftHand"]
        return self.rig.point(n, (float(a["posX"]), float(a["posY"]), float(a["posZ"])), pose)

    def sole_corners(self, side, pose):
        out = []
        for s in LEG[side][3]:
            m = self.rig.matrix(s, pose)
            e = self.rig.elements[s]
            size = [e["to"][i] - e["from"][i] for i in range(3)]
            out += [kin.apply(m, (a * size[0], 0.0, c * size[2])) for a in (0, 1) for c in (0, 1)]
        return out

    def leg_points(self, side, pose):
        ms = self.rig.all_matrices(pose)
        out = []
        stack = [LEG[side][0]]
        while stack:
            n = stack.pop()
            out += self.rig.corners(n, ms[n])
            stack += [c["name"] for c in self.rig.elements[n].get("children", [])]
        return out

    def flatten_foot(self, side, pose):
        foot = LEG[side][2]
        start = pose.get(foot, {}).get("rot", (0.0, 0.0, 0.0))

        def cost(v):
            p = dict(pose)
            p[foot] = dict(pose.get(foot, {}), rot=(v[0], start[1], v[1]))
            ys = [c[1] for c in self.sole_corners(side, p)]
            return (max(ys) - min(ys)) ** 2

        (x, z), _ = kin.solve(cost, [start[0], start[2]], [(-60, 60), (-90, 90)],
                              steps=(8.0, 4.0, 2.0, 1.0, 0.5, 0.25, 0.1, 0.05))
        pose[foot] = dict(pose.get(foot, {}), rot=(x, start[1], z))

    def lowest(self, pose, how):
        if how in ("feet", "sole"):
            return min(c[1] for s in "RL" for c in self.sole_corners(s, pose))
        return min(c[1] for s in "RL" for c in self.leg_points(s, pose))

    def ground(self, pose, how, clear=0.0):
        """Move the hips straight down (or up) until the lowest sole (or leg part) is at y 0. A key's
        offset is applied in the element's own turned frame, so the move is turned into it."""
        hip = pose.setdefault("hip-inside", {})
        hip.setdefault("rot", (0.0, 0.0, 0.0))
        for _ in range(4):
            dy = clear - self.lowest(pose, how)
            if abs(dy) < 1e-5:
                break
            rt = kin.transpose3(kin.rotation_of(self.rig.matrix("hip-inside", pose)))
            off = hip.get("off", (0.0, 0.0, 0.0))
            hip["off"] = tuple(off[i] + rt[i][1] * dy for i in range(3))

    def place_anchor(self, name, pose, spec):
        if spec[0] == "rest":
            pose[name] = ro()
            return
        _, pos, yaw = spec
        parent = self.rig.parent[name]
        e = self.rig.elements[name]
        want = kin.mul(kin.translate(*(v / 16 for v in pos)), kin.rot_xyz(0, yaw, 0))
        local = kin.mul(kin.invert_rigid(self.rig.matrix(parent, pose)), want)
        rot = kin.euler_xyz(local)
        rt = kin.transpose3(kin.rotation_of(local))
        t = [local[i][3] * 16 - e["from"][i] for i in range(3)]
        off = tuple(sum(rt[i][k] * t[k] for k in range(3)) for i in range(3))
        pose[name] = ro(rot, off)

    def grip_target(self, spec, pose):
        if spec[0] == "world":
            return spec[1]
        anchor, local = spec
        n, a = self.ap[anchor]
        return kin.apply(self.rig.attachment(n, a, pose), local)

    def reach(self, side, target, pose, hint):
        upper, lower, _ = ARM[side]
        h = tuple(float(v) for v in (hint or (0.0, 0.0, -30.0, -30.0)))
        bounds = [(-25, 95) if side == "R" else (-95, 25), (-70, 70), (-200, 60), (-145, 0)]

        def setp(v):
            p = dict(pose)
            p[upper] = dict(pose.get(upper, {}), rot=(v[0], v[1], v[2]))
            p[lower] = dict(pose.get(lower, {}), rot=(0.0, 0.0, v[3]))
            return p

        def cost(v):
            d = kin.dist(self.grip_point(side, setp(v)), target)
            reg = sum((v[i] - h[i]) ** 2 for i in range(4))
            return d * d + 0.0004 * reg

        v, _ = kin.solve(cost, list(h), bounds)
        pose.update(setp(v))

    # -- keys and animations ------------------------------------------------------------------
    def build_key(self, key):
        pose = merge(key.pose)
        if key.plant:
            for s in "RL":
                if key.plant == "feet":
                    self.flatten_foot(s, pose)
            self.ground(pose, key.plant, key.clear)
        for name, spec in key.anchors.items():
            self.place_anchor(name, pose, spec)
        for side, spec in key.grips.items():
            self.reach(side, self.grip_target(spec, pose), pose, key.hint.get(side))
        return pose

    # -- in-between keys ------------------------------------------------------------------------
    def errors(self, key0, key1, pose):
        """What should hold between two keys, and how far a pose is from it (voxels)."""
        worst = 0.0
        if key0.plant and key0.plant == key1.plant and key0.clear == key1.clear:
            worst = max(worst, abs(self.lowest(pose, key0.plant) - key0.clear))
        for name, spec in key0.anchors.items():
            if spec[0] == "world" and key1.anchors.get(name) == spec:
                n, a = next((n, a) for n, a in self.ap.values() if n == name)
                at = kin.apply(self.rig.attachment(n, a, pose), (0.0, 0.0, 0.0))
                worst = max(worst, kin.dist(at, spec[1]))
        for side, spec in key0.grips.items():
            if key1.grips.get(side) == spec:
                worst = max(worst, kin.dist(self.grip_point(side, pose), self.grip_target(spec, pose)))
        return worst

    def refine(self, k0, p0, k1, p1, depth=0, tolerance=0.4):
        """Keys between two built keys wherever the game's interpolation between them lets a planted
        foot, an object resting on the ground or a grip drift by more than the tolerance."""
        if k1.frame - k0.frame < 2 or depth > 5:
            return []
        fm = (k0.frame + k1.frame) // 2
        pm = lerp_pose(p0, p1, (fm - k0.frame) / (k1.frame - k0.frame))
        if self.errors(k0, k1, pm) <= tolerance:
            return []
        hint = {}
        for side in "RL":
            upper, lower, _ = ARM[side]
            u = pm.get(upper, {}).get("rot", (0.0, 0.0, 0.0))
            hint[side] = (u[0], u[1], u[2], pm.get(lower, {}).get("rot", (0.0, 0.0, 0.0))[2])
        km = Key(fm, pm,
                 plant=k0.plant if k0.plant == k1.plant and k0.clear == k1.clear else None, clear=k0.clear,
                 anchors={n: sp for n, sp in k0.anchors.items() if sp[0] == "world" and k1.anchors.get(n) == sp},
                 grips={s: sp for s, sp in k0.grips.items() if k1.grips.get(s) == sp},
                 hint=hint)
        pmb = self.build_key(km)
        return (self.refine(k0, p0, km, pmb, depth + 1, tolerance) + [(km, pmb)]
                + self.refine(km, pmb, k1, p1, depth + 1, tolerance))

    def animation(self, code, name, frames, keys, end, stopped):
        built = [(k, self.build_key(k)) for k in keys]
        full = []
        for i, (k, p) in enumerate(built):
            full.append((k, p))
            if i + 1 < len(built):
                full += self.refine(k, p, *built[i + 1])
            elif end == "Repeat" and len(built) > 1:
                k0, p0 = built[0]
                wrap = copy.copy(k0)
                wrap.frame = k0.frame + frames
                full += self.refine(k, p, wrap, p0)
        poses = [(k.frame, p) for k, p in full]
        # An element (or one of its groups) at rest in every key is left out: it would only pin
        # the element to rest and keep other animations from blending through it.
        groups = {}
        for _, p in poses:
            for n, k in p.items():
                for g, vals in k.items():
                    rest = 1.0 if g == "str" else 0.0
                    if any(num(v) != rest for v in vals):
                        groups.setdefault(n, set()).add(g)
        order = [n for n in self.rig.order if n in groups]
        keyframes = []
        for f, p in poses:
            els = {}
            for n in order:
                k = p.get(n, {})
                entry = {}
                for g in ("off", "rot", "str"):
                    if g in groups[n]:
                        vals = k.get(g, (1.0, 1.0, 1.0) if g == "str" else (0.0, 0.0, 0.0))
                        for an, v in zip(kin.GROUPS[g], vals):
                            entry[an] = num(v)
                els[n] = entry
            keyframes.append({"frame": f, "elements": els})
        return {"name": name, "code": code, "quantityframes": frames,
                "onActivityStopped": stopped, "onAnimationEnd": end, "keyframes": keyframes}


def lerp_pose(a, b, t):
    out = {}
    for n in set(a) | set(b):
        ka, kb = a.get(n, {}), b.get(n, {})
        entry = {}
        for g in set(ka) | set(kb):
            d = (1.0, 1.0, 1.0) if g == "str" else (0.0, 0.0, 0.0)
            va, vb = ka.get(g, d), kb.get(g, d)
            entry[g] = tuple(va[i] + (vb[i] - va[i]) * t for i in range(3))
        out[n] = entry
    return out


def num(v, places=3):
    v = round(float(v), places)
    return 0.0 if v == 0 else v


# ---------------------------------------------------------------------------------------------
# The vanilla walk, as full poses at its keys (exact: VS interpolates each element linearly).

def walk_keys(vanilla_anim):
    frames = [k["frame"] for k in vanilla_anim["keyframes"]]
    out = []
    for f in frames:
        p = kin.sample(vanilla_anim, f)
        p = {n: {g: v for g, v in k.items()} for n, k in p.items() if k and n != "origin"}
        out.append((f, p))
    return out


def without(pose, names):
    return {n: k for n, k in pose.items() if n not in names}


ARM_ELEMENTS = {s: set(ARM[s]) | HAND_ELEMENTS[s] for s in "RL"}


def bent_legs(thigh, knee, spread=0.0):
    """Both legs bent alike: thigh (rotZ, − forward), knee (rotZ, + flexes), spread (out, deg)."""
    return {"upperlegR": r(spread, 0, thigh), "lowerlegR": r(0, 0, knee),
            "upperlegL": r(-spread, 0, thigh), "lowerlegL": r(0, 0, knee)}


def authored(b: Builder, vanilla_anims):
    walk = vanilla_anims["stand-walk"]
    anims = []
    trunk_spec = ("rest",)
    carry_spec = ("rest",)

    block_grip, trunk_top_grip, trunk_near_grip = BLOCK_GRIPS, TRUNK_TOP_GRIPS, TRUNK_NEAR_GRIPS
    carry_body = {"head-inside": r(0, 0, 6), **FIST["R"], **FIST["L"]}
    hint_carry = {"R": (5, 0, -45, -60), "L": (-5, 0, -45, -60)}

    # carry-idle: the block in both arms; a slow breath.
    k_carry = lambda f, breath: Key(f, merge(carry_body, {"chest-inside": r(0, 0, breath),  # noqa: E731
                                                          "hip-inside": ro((0, 0, 0), (0, -0.4 * breath, 0))}),
                                    plant="feet", anchors={"carry-anchor": carry_spec}, grips=block_grip,
                                    hint=hint_carry)
    anims.append(b.animation("carry-idle", "Carry-Idle", 60, [k_carry(0, 0.0), k_carry(30, 1.5)],
                             "Repeat", "EaseOut"))

    # carry-walk: the vanilla walk's legs, hips and skirts; the arms hold the block.
    keys = []
    for f, p in walk_keys(walk):
        base = without(p, ARM_ELEMENTS["R"] | ARM_ELEMENTS["L"] | {"head-inside"})
        keys.append(Key(f, merge(base, carry_body), plant="sole", anchors={"carry-anchor": carry_spec},
                        grips=block_grip, hint=hint_carry))
    anims.append(b.animation("carry-walk", "Carry-Walk", walk["quantityframes"], keys, "Repeat", "EaseOut"))

    # lift: from the ground in front into carry-idle's first frame (held).
    ground_block = ("world", BLOCK_GROUND, 0.0)
    squat = merge(bent_legs(-62, 95, 6), {"hip-inside": r(0, 0, 28), "chest-inside": r(0, 0, 30),
                                         "head-inside": r(0, 0, -20), "chainskirt-front1": r(0, 0, -40),
                                         "chainskirt-back1": r(0, 0, 20)})
    half = merge(bent_legs(-30, 45, 3), {"hip-inside": r(0, 0, 12), "chest-inside": r(0, 0, 12),
                                        "head-inside": r(0, 0, -5), "chainskirt-front1": r(0, 0, -20)})
    hint_low = {"R": (10, 0, -35, -20), "L": (-10, 0, -35, -20)}
    lift = [
        Key(0, {}, plant="feet", anchors={"carry-anchor": ground_block}),
        Key(8, half, plant="feet", anchors={"carry-anchor": ground_block}),
        Key(16, merge(squat, {**FIST["R"], **FIST["L"]}), plant="feet", anchors={"carry-anchor": ground_block},
            grips=block_grip, hint=hint_low),
        Key(22, merge(squat, {"hip-inside": r(0, 0, 30)}, FIST["R"], FIST["L"]), plant="feet",
            anchors={"carry-anchor": ground_block}, grips=block_grip, hint=hint_low),
        Key(34, merge(half, FIST["R"], FIST["L"]), plant="feet",
            anchors={"carry-anchor": ("world", (-9.0, 16.0, CENTRE_Z), 0.0)}, grips=block_grip, hint=hint_low),
        k_carry(44, 0.0),
    ]
    anims.append(b.animation("lift", "Lift", 45, lift, "Hold", "PlayTillEnd"))

    # setdown: carry-idle's first frame to the block on the ground, then back to rest.
    setdown = [
        k_carry(0, 0.0),
        Key(12, merge(half, FIST["R"], FIST["L"]), plant="feet",
            anchors={"carry-anchor": ("world", (-9.0, 16.0, CENTRE_Z), 0.0)}, grips=block_grip, hint=hint_low),
        Key(24, merge(squat, {"hip-inside": r(0, 0, 30)}, FIST["R"], FIST["L"]), plant="feet",
            anchors={"carry-anchor": ground_block}, grips=block_grip, hint=hint_low),
        Key(28, squat, plant="feet", anchors={"carry-anchor": ground_block}, grips=block_grip, hint=hint_low),
        Key(36, half, plant="feet", anchors={"carry-anchor": ground_block}),
        Key(44, {}, plant="feet", anchors={"carry-anchor": ground_block}),
    ]
    anims.append(b.animation("setdown", "Set-Down", 45, setdown, "Stop", "PlayTillEnd"))

    # Trunk carrying: on the left shoulder, the left hand steadying it, the head leaning away.
    trunk_body = {"head-inside": r(-14, 0, 4), "upperarmL": ro((0, 0, 0), (0, 1.0, 0)), **FIST["L"]}
    hint_shoulder = {"L": (-20, 20, -150, -100)}
    k_trunk = lambda f, breath: Key(f, merge(trunk_body, {"chest-inside": r(0, 0, breath),  # noqa: E731
                                                          "hip-inside": ro((0, 0, 0), (0, -0.4 * breath, 0)),
                                                          "upper-armR": r(0, 0, 4), "lower-armR": r(0, 0, -12)}),
                                    plant="feet", anchors={"trunk-anchor": trunk_spec}, grips=SHOULDER_GRIPS,
                                    hint=hint_shoulder)
    anims.append(b.animation("trunk-carry-idle", "Trunk-Carry-Idle", 60, [k_trunk(0, 0.0), k_trunk(30, 1.5)],
                             "Repeat", "EaseOut"))

    keys = []
    for f, p in walk_keys(walk):
        base = without(p, ARM_ELEMENTS["L"] | {"head-inside"})
        head = p.get("head-inside", {}).get("rot", (0, 0, 0))
        keys.append(Key(f, merge(base, trunk_body, {"head-inside": r(-14 + head[0], head[1], 4 + head[2])}),
                        plant="sole", anchors={"trunk-anchor": trunk_spec}, grips=SHOULDER_GRIPS,
                        hint=hint_shoulder))
    anims.append(b.animation("trunk-carry-walk", "Trunk-Carry-Walk", walk["quantityframes"], keys,
                             "Repeat", "EaseOut"))

    # trunk-pickup: a thin trunk lying across in front; squat, take it by the top, stand with it
    # across the chest, then turn it front to back and heave it onto the left shoulder.
    across = -90.0  # the anchor's yaw that lays the trunk's length along model z
    ground_trunk = ("world", TRUNK_GROUND, across)
    deep = merge(bent_legs(-66, 100, 8), {"hip-inside": r(0, 0, 30), "chest-inside": r(0, 0, 34),
                                         "head-inside": r(0, 0, -22), "chainskirt-front1": r(0, 0, -40),
                                         "chainskirt-back1": r(0, 0, 20)})
    chest_trunk = ("world", (-7.0, 30.0, CENTRE_Z), across)
    heave_trunk = ("world", (0.0, 47.0, 15.0), -40.0)
    hint_heave = {"R": (10, 0, -90, -60), "L": (-10, 0, -120, -80)}
    pickup = [
        Key(0, {}, plant="feet", anchors={"trunk-anchor": ground_trunk}),
        Key(10, half, plant="feet", anchors={"trunk-anchor": ground_trunk}),
        Key(18, merge(deep, FIST["R"], FIST["L"]), plant="feet", anchors={"trunk-anchor": ground_trunk},
            grips=trunk_top_grip, hint=hint_low),
        Key(24, merge(deep, {"hip-inside": r(0, 0, 32)}, FIST["R"], FIST["L"]), plant="feet",
            anchors={"trunk-anchor": ground_trunk}, grips=trunk_top_grip, hint=hint_low),
        Key(36, merge(bent_legs(-14, 22, 4), {"hip-inside": r(0, 0, 4), "chest-inside": r(0, 0, -4)},
                      FIST["R"], FIST["L"]), plant="feet",
            anchors={"trunk-anchor": chest_trunk}, grips=trunk_near_grip, hint=hint_carry),
        Key(48, merge(bent_legs(-20, 30, 4), {"chest-inside": r(-8, -10, -6), "head-inside": r(-10, 0, 0)},
                      FIST["R"], FIST["L"]), plant="feet",
            anchors={"trunk-anchor": heave_trunk}, grips=TRUNK_HEAVE_GRIPS,
            hint=hint_heave),
        k_trunk(59, 0.0),
    ]
    anims.append(b.animation("trunk-pickup", "Trunk-Pickup", 60, pickup, "Hold", "PlayTillEnd"))

    tsetdown = [
        k_trunk(0, 0.0),
        Key(12, merge(bent_legs(-20, 30, 4), {"chest-inside": r(-8, -10, -6), "head-inside": r(-10, 0, 0)},
                      FIST["R"], FIST["L"]), plant="feet",
            anchors={"trunk-anchor": heave_trunk}, grips=TRUNK_HEAVE_GRIPS,
            hint=hint_heave),
        Key(22, merge(bent_legs(-14, 22, 4), {"hip-inside": r(0, 0, 4), "chest-inside": r(0, 0, -4)},
                      FIST["R"], FIST["L"]), plant="feet",
            anchors={"trunk-anchor": chest_trunk}, grips=trunk_near_grip, hint=hint_carry),
        Key(32, merge(deep, {"hip-inside": r(0, 0, 32)}, FIST["R"], FIST["L"]), plant="feet",
            anchors={"trunk-anchor": ground_trunk}, grips=trunk_top_grip, hint=hint_low),
        Key(36, deep, plant="feet", anchors={"trunk-anchor": ground_trunk}, grips=trunk_top_grip, hint=hint_low),
        Key(44, half, plant="feet", anchors={"trunk-anchor": ground_trunk}),
        Key(49, {}, plant="feet", anchors={"trunk-anchor": ground_trunk}),
    ]
    anims.append(b.animation("trunk-setdown", "Trunk-Set-Down", 50, tsetdown, "Stop", "PlayTillEnd"))

    # Thick trunks: across the body in both arms, low against the belly, leaning back against the
    # weight, the head bowed to look over it. Standing, the trunk rides with the torso (the anchor at
    # rest); walking, see below.
    thick_spec = ("rest",)
    thick_body = {"chest-inside": r(0, 0, -5), "head-inside": r(0, 0, 10), **FIST["R"], **FIST["L"]}
    hint_thick = {"R": (8, 0, -20, -20), "L": (-8, 0, -20, -20)}
    k_thick = lambda f, breath: Key(f, merge(thick_body, bent_legs(-8, 14, 4), {  # noqa: E731
        "chest-inside": r(0, 0, -5 + breath), "hip-inside": ro((0, 0, -3), (0, -0.4 * breath, 0))}),
        plant="feet", anchors={"thick-trunk-anchor": thick_spec}, grips=THICK_GRIPS, hint=hint_thick)
    anims.append(b.animation("trunk-thick-carry-idle", "Trunk-Thick-Carry-Idle", 60,
                             [k_thick(0, 0.0), k_thick(30, 1.0)], "Repeat", "EaseOut"))

    # Walking, the trunk would swing 8 voxels a step if it rode the walk's hip pitch this far out in
    # front; the arms take the pitch instead, and the trunk keeps the idle's place, rising and falling
    # only with the hips.
    idle = b.build_key(k_thick(0, 0.0))
    held = kin.apply(b.rig.attachment(*b.ap["ThickTrunk"], idle), (0.0, 0.0, 0.0))
    keys = []
    for f, p in walk_keys(walk):
        base = without(p, ARM_ELEMENTS["R"] | ARM_ELEMENTS["L"] | {"head-inside"})
        chest = base.get("chest-inside", {}).get("rot", (0, 0, 0))
        bob = p.get("hip-inside", {}).get("off", (0, 0, 0))[1]
        keys.append(Key(f, merge(base, thick_body, {"chest-inside": r(chest[0], chest[1], chest[2] - 5)}),
                        plant="sole", grips=THICK_GRIPS, hint=hint_thick,
                        anchors={"thick-trunk-anchor": ("world", (held[0], held[1] + bob, held[2]), 0.0)}))
    anims.append(b.animation("trunk-thick-carry-walk", "Trunk-Thick-Carry-Walk", walk["quantityframes"], keys,
                             "Repeat", "EaseOut"))

    # trunk-thick-pickup: lying across in front of the feet. A trunk two blocks high would take the
    # head if the body bowed over it, so it squats deep with the knees out and the head up, takes it
    # by its near side low down (the fists under the edge), lifts it with the legs and stands, leaning
    # back as it comes up against the belly.
    ground_thick = ("world", THICK_GROUND, 0.0)
    rising = ("world", THICK_RISING, 0.0)
    approach = merge(bent_legs(-30, 50, 8), {"hip-inside": r(0, 0, 12), "chest-inside": r(0, 0, 10),
                                            "head-inside": r(0, 0, -20), "chainskirt-front1": r(0, 0, -20)})
    squat_thick = merge(bent_legs(-72, 128, 14), {"hip-inside": r(0, 0, 17), "chest-inside": r(0, 0, 16),
                                                 "head-inside": r(0, 0, -50), "chainskirt-front1": r(0, 0, -40),
                                                 "chainskirt-back1": r(0, 0, 20)}, FIST["R"], FIST["L"])
    lifting = merge(bent_legs(-46, 77, 9), {"hip-inside": r(0, 0, 8), "chest-inside": r(0, 0, 22),
                                           "head-inside": r(0, 0, -41), "chainskirt-front1": r(0, 0, -25)},
                    FIST["R"], FIST["L"])
    risen = merge(bent_legs(-26, 42, 6), {"hip-inside": r(0, 0, 2), "chest-inside": r(0, 0, 8),
                                         "head-inside": r(0, 0, -12), "chainskirt-front1": r(0, 0, -10)},
                  FIST["R"], FIST["L"])
    hint_reach = {"R": (12, 0, -40, -10), "L": (-12, 0, -40, -10)}
    tpickup = [
        Key(0, {}, plant="feet", anchors={"thick-trunk-anchor": ground_thick}),
        Key(12, approach, plant="feet", anchors={"thick-trunk-anchor": ground_thick}),
        Key(22, squat_thick, plant="feet", anchors={"thick-trunk-anchor": ground_thick}, grips=THICK_GRIPS,
            hint=hint_reach),
        Key(26, merge(squat_thick, {"hip-inside": r(0, 0, 18)}), plant="feet",
            anchors={"thick-trunk-anchor": ground_thick}, grips=THICK_GRIPS, hint=hint_reach),
        Key(40, lifting, plant="feet", anchors={"thick-trunk-anchor": rising}, grips=THICK_GRIPS,
            hint=hint_reach),
        Key(50, risen, plant="feet", anchors={"thick-trunk-anchor": ("world", THICK_RISEN, 0.0)},
            grips=THICK_GRIPS, hint=hint_thick),
        k_thick(59, 0.0),
    ]
    anims.append(b.animation("trunk-thick-pickup", "Trunk-Thick-Pickup", 60, tpickup, "Hold", "PlayTillEnd"))

    tsetdown_thick = [
        k_thick(0, 0.0),
        Key(8, risen, plant="feet", anchors={"thick-trunk-anchor": ("world", THICK_RISEN, 0.0)},
            grips=THICK_GRIPS, hint=hint_thick),
        Key(16, lifting, plant="feet", anchors={"thick-trunk-anchor": rising}, grips=THICK_GRIPS,
            hint=hint_reach),
        Key(28, merge(squat_thick, {"hip-inside": r(0, 0, 18)}), plant="feet",
            anchors={"thick-trunk-anchor": ground_thick}, grips=THICK_GRIPS, hint=hint_reach),
        Key(32, squat_thick, plant="feet", anchors={"thick-trunk-anchor": ground_thick}, grips=THICK_GRIPS,
            hint=hint_reach),
        Key(42, approach, plant="feet", anchors={"thick-trunk-anchor": ground_thick}),
        Key(49, {}, plant="feet", anchors={"thick-trunk-anchor": ground_thick}),
    ]
    anims.append(b.animation("trunk-thick-setdown", "Trunk-Thick-Set-Down", 50, tsetdown_thick, "Stop",
                             "PlayTillEnd"))

    # fell: a two-handed chop from over the right shoulder into a trunk in front, looping. The cut
    # lands on frame FELL_IMPACT_FRAME.
    hint_up = {"R": (40, 0, -150, -70), "L": (-10, 30, -150, -80)}
    hint_down = {"R": (0, 0, -60, -20), "L": (0, 20, -60, -30)}
    grip = lambda pr, pl: {"R": ("world", pr), "L": ("world", pl)}  # noqa: E731
    fists = merge(FIST["R"], FIST["L"])
    fell = [
        Key(0, merge(fists, bent_legs(-6, 8, 5), {"hip-inside": r(0, 6, -4), "chest-inside": r(0, 14, -10),
                                                  "head-inside": r(0, -14, 8), "chainskirt-front1": r(0, 0, 5)}),
            plant="feet", grips=grip((10.0, 62.0, -6.0), (8.5, 58.0, -2.5)), hint=hint_up),
        Key(9, merge(fists, bent_legs(-8, 12, 5), {"hip-inside": r(0, 2, 0), "chest-inside": r(0, 6, 0),
                                                   "head-inside": r(0, -6, 10)}),
            plant="feet", grips=grip((-2.0, 60.0, 0.0), (0.5, 56.0, 2.5)), hint=hint_up),
        Key(15, merge(fists, bent_legs(-18, 28, 5), {"hip-inside": r(0, -2, 12), "chest-inside": r(0, -4, 18),
                                                     "head-inside": r(0, 0, 0), "chainskirt-front1": r(0, 0, -15)}),
            plant="feet", grips=grip(FELL_IMPACT_R, FELL_IMPACT_L), hint=hint_down),
        Key(20, merge(fists, bent_legs(-20, 31, 5), {"hip-inside": r(0, -2, 13), "chest-inside": r(0, -4, 19),
                                                     "head-inside": r(0, 0, 0), "chainskirt-front1": r(0, 0, -15)}),
            plant="feet", grips=grip(tuple(v + d for v, d in zip(FELL_IMPACT_R, (0.5, -0.5, 0))),
                                     tuple(v + d for v, d in zip(FELL_IMPACT_L, (0.5, -0.5, 0)))), hint=hint_down),
        Key(30, merge(fists, bent_legs(-10, 14, 5), {"hip-inside": r(0, 2, 4), "chest-inside": r(0, 6, 4),
                                                     "head-inside": r(0, -6, 6)}),
            plant="feet", grips=grip((-4.0, 46.0, 1.0), (-1.0, 43.0, 3.5)), hint=hint_down),
    ]
    anims.append(b.animation("fell", "Fell", 40, fell, "Repeat", "EaseOut"))

    # guard-idle: an alert stance, knees bent, the axe hand up, the head sweeping side to side.
    guard_body = merge(bent_legs(-14, 24, 9), FIST["R"], FIST["L"],
                       {"upper-armR": r(10, 0, -25), "lower-armR": r(0, 0, -75), "wristR": r(0, 0, -10),
                        "upperarmL": r(-12, 0, -30), "lower-armL": r(0, 0, -85),
                        "chainskirt-front1": r(0, 0, -8)})
    g = lambda f, yaw, lean, breath: Key(f, merge(guard_body, {  # noqa: E731
        "hip-inside": r(0, 0, lean), "chest-inside": r(0, -yaw * 0.25, 6 + breath),
        "head-inside": r(0, yaw, -6)}), plant="feet")
    anims.append(b.animation("guard-idle", "Guard-Idle", 80,
                             [g(0, 0, 4, 0), g(16, 32, 4, 1), g(30, 32, 4.5, 1.5), g(40, 0, 4, 0.5),
                              g(56, -32, 4, 1), g(70, -32, 4.5, 1.5)], "Repeat", "EaseOut"))

    # hung: limp in the gantry, held up by the back, feet clear of the floor. One frame, held.
    hung = merge(LIMP["R"], LIMP["L"], {
        "hip-inside": r(0, 0, 4), "chest-inside": r(0, 0, 14), "head-inside": r(0, 0, 32),
        "upper-armR": r(4, 0, -12), "lower-armR": r(0, 0, -8), "upperarmL": r(-4, 0, -12), "lower-armL": r(0, 0, -8),
        "upperlegR": r(0, 0, -6), "lowerlegR": r(0, 0, 12), "footR": r(0, 0, 34),
        "upperlegL": r(0, 0, -2), "lowerlegL": r(0, 0, 8), "footL": r(0, 0, 30),
        "chainskirt-front1": r(0, 0, 4), "chainskirt-back1": r(0, 0, -4)})
    anims.append(b.animation("hung", "Hung", 1, [Key(0, hung, plant="sole", clear=HUNG_CLEAR)], "Hold", "EaseOut"))

    # activate: the first awakening in the gantry, from hung to standing.
    anims.append(b.animation("activate", "Activate", 90, [
        Key(0, hung, plant="sole", clear=HUNG_CLEAR),
        Key(14, merge(hung, {"head-inside": r(0, 8, 26)}), plant="sole", clear=HUNG_CLEAR),
        Key(26, merge(hung, {"head-inside": r(0, -6, 20)}, scaled(LIMP["R"], 0.2), scaled(LIMP["L"], 0.2)),
            plant="sole", clear=HUNG_CLEAR),
        Key(44, merge(bent_legs(-24, 40, 3), {"hip-inside": r(0, 0, 10), "chest-inside": r(0, 0, 14),
                                             "head-inside": r(0, 0, 18), "upper-armR": r(4, 0, -6),
                                             "upperarmL": r(-4, 0, -6)}), plant="feet"),
        Key(60, merge(bent_legs(-12, 20, 2), {"hip-inside": r(0, 0, 4), "chest-inside": r(0, 0, 4),
                                             "head-inside": r(0, 0, 0)}), plant="feet"),
        Key(74, merge({"head-inside": r(0, 28, -4), "chest-inside": r(0, 6, 0)}), plant="feet"),
        Key(82, merge({"head-inside": r(0, -20, -2), "chest-inside": r(0, -4, 0)}), plant="feet"),
        Key(89, {}, plant="feet"),
    ], "Stop", "PlayTillEnd"))

    # slump: at 0 HP the knees give and it sinks onto them, bowed and limp, and stays there.
    kneel = merge(LIMP["R"], LIMP["L"], {
        "hip-inside": r(0, 0, 16), "chest-inside": r(0, 0, 30), "head-inside": r(0, 0, 38),
        "upper-armR": r(6, 0, -20), "lower-armR": r(0, 0, -10), "upperarmL": r(-6, 0, -20), "lower-armL": r(0, 0, -10),
        "upperlegR": r(4, 0, -32), "lowerlegR": r(0, 0, 116), "footR": r(0, 0, 50),
        "upperlegL": r(-4, 0, -32), "lowerlegL": r(0, 0, 116), "footL": r(0, 0, 50),
        "chainskirt-front1": r(0, 0, -30), "chainskirt-back1": r(0, 0, 25), "chainskirt-back2": r(0, 0, 20)})
    anims.append(b.animation("slump", "Slump", 50, [
        Key(0, {}, plant="feet"),
        Key(8, merge(bent_legs(-24, 42, 2), {"hip-inside": r(0, 0, 6), "chest-inside": r(0, 0, 10),
                                            "head-inside": r(0, 0, 12)}, scaled(LIMP["R"], 0.5),
                     scaled(LIMP["L"], 0.5)), plant="feet"),
        Key(20, merge(scaled(kneel, 0.8), {"hip-inside": r(0, 0, 8)}), plant="legs"),
        Key(32, merge(kneel, {"chest-inside": r(0, 0, 36), "head-inside": r(0, 0, 44)}), plant="legs"),
        Key(40, merge(kneel, {"chest-inside": r(0, 0, 33)}), plant="legs"),
        Key(49, kneel, plant="legs"),
    ], "Hold", "PlayTillEnd"))

    # standup: from slump's last frame, up onto one knee, then standing.
    one_knee = merge({"hip-inside": r(0, 0, 8), "chest-inside": r(0, 0, 14), "head-inside": r(0, 0, 4),
                      "upperlegR": r(4, 0, -4), "lowerlegR": r(0, 0, 92), "footR": r(0, 0, 40),
                      "upperlegL": r(-4, 0, -84), "lowerlegL": r(0, 0, 84),
                      "upper-armR": r(4, 0, -8), "upperarmL": r(-10, 0, -40), "lower-armL": r(0, 0, -50)},
                     FIST["L"])
    anims.append(b.animation("standup", "Stand-Up", 60, [
        Key(0, kneel, plant="legs"),
        Key(14, merge(scaled(kneel, 0.85), {"chest-inside": r(0, 0, 10), "head-inside": r(0, 0, 0)}), plant="legs"),
        Key(30, one_knee, plant="legs"),
        Key(44, merge(bent_legs(-30, 50, 3), {"hip-inside": r(0, 0, 10), "chest-inside": r(0, 0, 10),
                                             "upperarmL": r(-4, 0, -10), "lower-armL": r(0, 0, -20)}), plant="feet"),
        Key(59, {}, plant="feet"),
    ], "Stop", "PlayTillEnd"))

    return anims


FELL_IMPACT_FRAME = 15
# Event frames the gameplay can key off (the C# has no other way to know them).
EVENTS = {
    "fell": {"impact": FELL_IMPACT_FRAME},
    "lift": {"grab": 22},
    "setdown": {"release": 28},
    "trunk-pickup": {"grab": 24},
    "trunk-setdown": {"release": 36},
    "trunk-thick-pickup": {"grab": 26},
    "trunk-thick-setdown": {"release": 32},
}


# ---------------------------------------------------------------------------------------------

def anchor_element(name, at):
    return {"name": name, "from": list(at), "to": list(at), "rotationOrigin": list(at),
            "faces": {f: {"texture": "#null", "uv": [0.0, 0.0, 0.0, 0.0], "enabled": False}
                      for f in ("north", "east", "south", "west", "up", "down")}}


def round_list(v, places=3):
    return [num(x, places) for x in v]


def build(vanilla, vanilla_look=False):
    shape = copy.deepcopy(vanilla)
    shape.pop("editor", None)
    codes = [a["code"] for a in vanilla["animations"]]
    if sorted(codes) != sorted(KEEP + list(DROP)):
        raise SystemExit(f"vanilla animations changed: {sorted(set(codes) ^ set(KEEP + list(DROP)))}")
    if set(shape["textures"]) != set(VANILLA_TEXTURES) or any(
            shape["textures"][k] != v for k, v in VANILLA_TEXTURES.items()):
        raise SystemExit("vanilla texture map changed")
    shape["textures"] = {k: "game:" + (v if vanilla_look else RESTYLE.get(k, v)) for k, v in VANILLA_TEXTURES.items()}
    shape["animations"] = [a for a in vanilla["animations"] if a["code"] in KEEP]

    rig = kin.Rig(shape)
    rest = rig.all_matrices({})
    chest = rig.elements["chest-inside"]
    to_chest = kin.invert_rigid(rest["chest-inside"])
    chest.setdefault("children", [])
    for name, world in (("carry-anchor", CARRY_REST), ("trunk-anchor", TRUNK_REST),
                        ("thick-trunk-anchor", THICK_REST)):
        chest["children"].append(anchor_element(name, round_list(kin.apply(to_chest, world))))

    # The hands: RightHand/LeftHand (the codes the game's held-item renderer looks up) at the palm,
    # turned so the frame at rest is the seraph's at rest (its ItemAnchor's, turned −180 about y),
    # so items sit in the hand as they do in a player's when the forearms are posed alike.
    flip = kin.rotation_of(kin.rot_xyz(0, -180, 0))
    for wrist, palm, code in (("wristR", "palmR", "RightHand"), ("wristL", "palmL", "LeftHand")):
        inv = kin.invert_rigid(rest[wrist])
        pos = kin.apply(inv, rig.centre(palm, rest[palm]))
        rot = kin.euler_xyz(kin.as4(kin.mul3(kin.transpose3(kin.rotation_of(rest[wrist])), flip)))
        rig.elements[wrist]["attachmentpoints"].append(
            {"code": code, "posX": num(pos[0]), "posY": num(pos[1]), "posZ": num(pos[2]),
             "rotationX": num(rot[0]), "rotationY": num(rot[1]), "rotationZ": num(rot[2])})
    rig = kin.Rig(shape)
    rig.elements["carry-anchor"]["attachmentpoints"] = [
        {"code": "Carry", "posX": 0.0, "posY": 0.0, "posZ": 0.0, "rotationX": 0.0, "rotationY": 0.0, "rotationZ": 0.0}]
    rig.elements["trunk-anchor"]["attachmentpoints"] = [
        {"code": "Trunk", "posX": 0.0, "posY": 0.0, "posZ": 0.0, "rotationX": 0.0, "rotationY": 90.0, "rotationZ": 0.0}]
    rig.elements["thick-trunk-anchor"]["attachmentpoints"] = [
        {"code": "ThickTrunk", "posX": 0.0, "posY": 0.0, "posZ": 0.0, "rotationX": 0.0, "rotationY": 0.0, "rotationZ": 0.0}]

    b = Builder(shape)
    shape["animations"] += authored(b, {a["code"]: a for a in vanilla["animations"]})

    restyle = "" if vanilla_look else (" Restyled: " + ", ".join(
        f"#{k} {VANILLA_TEXTURES[k]} -> {v}" for k, v in RESTYLE.items()) + " (make_shape.py --vanilla-look undoes it).")
    out = {"_comment": "Generated by mods-src/seraphhorizons/Eidolon/tools/make_shape.py from Vintage Story's "
                       "entity/lore/eidolon/normal.json (game 1.22.7, Anomalous Games' model; see CREDITS.md). "
                       "Do not edit by hand." + restyle}
    out.update(shape)
    return out


def stage_map(shape):
    """{element: stage code}, by nearest claimed ancestor-or-self; raises on a bad claim list."""
    rig = kin.Rig(shape)
    claims = {}
    for st in STAGES:
        for root in st["roots"]:
            if root not in rig.elements:
                raise ValueError(f"stage {st['code']}: no element {root}")
            if root in claims:
                raise ValueError(f"{root} claimed by {claims[root]} and {st['code']}")
            claims[root] = st["code"]
    out = {}
    for n in rig.order:
        a = n
        while a is not None and a not in claims:
            a = rig.parent[a]
        if a is None:
            raise ValueError(f"{n} is in no stage")
        out[n] = claims[a]
    return out


def stages_file(shape):
    sm = stage_map(shape)
    rig = kin.Rig(shape)
    return {"_comment": "Generated by mods-src/seraphhorizons/Eidolon/tools/make_shape.py: the eidolon's build "
                        "stages, each with the shape elements (eidolon.json) it adds. Shown cumulatively. "
                        "Ingredients are a proposal (Eidolon/README.md).",
            "stages": [{"stage": st["stage"], "code": st["code"], "name": st["name"],
                        "ingredients": st["ingredients"],
                        "elements": [n for n in rig.order if sm[n] == st["code"]]} for st in STAGES]}


def check_stages(shape, stages):
    """Problems with a stages file against a shape: every element in exactly one stage, parents first."""
    rig = kin.Rig(shape)
    problems = []
    where = {}
    for st in stages["stages"]:
        for n in st["elements"]:
            if n not in rig.elements:
                problems.append(f"{st['code']}: no element {n}")
            if n in where:
                problems.append(f"{n} in {where[n][1]} and {st['code']}")
            where[n] = (st["stage"], st["code"])
    for n in rig.order:
        if n not in where:
            problems.append(f"{n} is in no stage")
        elif rig.parent[n] is not None and rig.parent[n] in where and where[rig.parent[n]][0] > where[n][0]:
            problems.append(f"{n} ({where[n][1]}) comes before its parent {rig.parent[n]} ({where[rig.parent[n]][1]})")
    return problems


# ---------------------------------------------------------------------------------------------
# Writing: strict JSON, tab-indented, a leaf object or array (one holding no object) on one line.

def dumps(v, depth=0):
    pad = "\t" * depth

    def nested(x):
        return isinstance(x, dict) or isinstance(x, list) and any(isinstance(y, (dict, list)) for y in x)

    if isinstance(v, dict) and (depth <= 1 or any(nested(x) for x in v.values())):
        inner = [f"{pad}\t{json.dumps(k)}: {dumps(x, depth + 1)}" for k, x in v.items()]
        return "{\n" + ",\n".join(inner) + f"\n{pad}}}"
    if isinstance(v, list) and any(isinstance(x, (dict, list)) for x in v):
        inner = [f"{pad}\t{dumps(x, depth + 1)}" for x in v]
        return "[\n" + ",\n".join(inner) + f"\n{pad}]"
    return json.dumps(v, separators=(", ", ": "), ensure_ascii=False)


def render(obj):
    return dumps(obj) + "\n"


# ---------------------------------------------------------------------------------------------
# Pose checks, from a written shape (what the game will play).

def frame_poses(anim):
    return [(f, kin.sample(anim, f)) for f in range(anim["quantityframes"])]


def report(shape):
    rig = kin.Rig(shape)
    b = Builder(shape)
    anims = {a["code"]: a for a in shape["animations"]}
    out = {}

    def feet(pose):
        return [min(c[1] for c in b.sole_corners(s, pose)) for s in "RL"]

    def keyed(code):
        return [k["frame"] for k in anims[code]["keyframes"]]

    # Lowest foot over every frame of the walk cycles (vanilla's walk for reference).
    for code in ("stand-walk", "carry-walk", "trunk-carry-walk", "trunk-thick-carry-walk"):
        lows = [min(feet(p)) for _, p in frame_poses(anims[code])]
        out[code + ": lowest sole y, min..max over frames"] = (round(min(lows), 2), round(max(lows), 2))
    # Planted keys: both soles on the ground.
    for code in ("fell", "guard-idle", "lift", "setdown", "trunk-pickup", "trunk-setdown", "trunk-thick-pickup",
                 "trunk-thick-setdown", "trunk-thick-carry-idle", "standup", "activate"):
        worst = 0.0
        for f in keyed(code):
            p = kin.sample(anims[code], f)
            ys = feet(p)
            if code in ("standup",) and f < 44:
                continue
            if code == "activate" and f < 44:
                continue
            worst = max(worst, max(abs(y) for y in ys))
        out[code + ": worst sole height at planted keys"] = round(worst, 2)
    # Hung: feet clear of the floor.
    out["hung: lowest sole y"] = round(min(feet(kin.sample(anims["hung"], 0))), 2)
    # Kneeling: lowest leg point.
    p = kin.sample(anims["slump"], 49)
    out["slump (held frame): lowest leg point y"] = round(min(c[1] for s in "RL" for c in b.leg_points(s, p)), 2)
    out["slump (held frame): head top y"] = round(max(c[1] for n in ("hood-top",) for c in rig.corners(n, rig.matrix(n, p))), 2)

    def grip_err(code, frames, grips):
        worst = 0.0
        for f in frames:
            p = kin.sample(anims[code], f)
            for side, spec in grips.items():
                t = b.grip_target(spec, p)
                worst = max(worst, kin.dist(b.grip_point(side, p), t))
        return round(worst, 2)

    block = BLOCK_GRIPS
    out["carry-idle: hand to block grip, every frame"] = grip_err("carry-idle", range(60), block)
    out["carry-walk: hand to block grip, every frame"] = grip_err("carry-walk", range(30), block)
    out["lift: hand to block grip, frames 16..44"] = grip_err("lift", range(16, 45), block)
    out["setdown: hand to block grip, frames 0..28"] = grip_err("setdown", range(0, 29), block)
    sh = SHOULDER_GRIPS
    out["trunk-carry-walk: left hand to trunk grip, every frame"] = grip_err("trunk-carry-walk", range(30), sh)
    out["trunk-carry-idle: left hand to trunk grip, every frame"] = grip_err("trunk-carry-idle", range(60), sh)
    top = TRUNK_TOP_GRIPS
    out["trunk-pickup: hands to trunk top, frames 18..24"] = grip_err("trunk-pickup", range(18, 25), top)
    out["trunk-setdown: hands to trunk top, frames 32..36"] = grip_err("trunk-setdown", range(32, 37), top)

    # Objects resting on the ground stay put while the body moves (the anchor is the chest's child).
    def drift(code, anchor, frames, want):
        n, a = b.ap[anchor]
        worst = 0.0
        for f in frames:
            m = rig.attachment(n, a, kin.sample(anims[code], f))
            worst = max(worst, kin.dist(kin.apply(m, (0, 0, 0)), want))
        return round(worst, 2)

    out["lift: block drift on the ground, frames 0..22"] = drift("lift", "Carry", range(0, 23), BLOCK_GROUND)
    out["setdown: block drift on the ground, frames 24..44"] = drift("setdown", "Carry", range(24, 45), BLOCK_GROUND)
    out["trunk-pickup: trunk drift on the ground, frames 0..24"] = drift("trunk-pickup", "Trunk", range(0, 25), TRUNK_GROUND)
    out["trunk-setdown: trunk drift on the ground, frames 32..49"] = drift("trunk-setdown", "Trunk", range(32, 50), TRUNK_GROUND)

    # Felling: the hands together on the handle, and where they are at the cut.
    p = kin.sample(anims["fell"], FELL_IMPACT_FRAME)
    hr, hl = b.grip_point("R", p), b.grip_point("L", p)
    out["fell: right hand at impact (x, y, z)"] = tuple(round(v, 1) for v in hr)
    out["fell: left hand at impact (x, y, z)"] = tuple(round(v, 1) for v in hl)
    gap = [kin.dist(b.grip_point("R", q), b.grip_point("L", q)) for _, q in frame_poses(anims["fell"])]
    out["fell: distance between the hands, min..max over frames"] = (round(min(gap), 1), round(max(gap), 1))
    top_y = [b.grip_point("R", kin.sample(anims["fell"], 0))[1]]
    out["fell: right hand height at wind-up"] = round(top_y[0], 1)

    # The trunk on the shoulder clears the head and hood.
    p = kin.sample(anims["trunk-carry-idle"], 0)
    n, a = b.ap["Trunk"]
    m = rig.attachment(n, a, p)
    inv = kin.invert_rigid(m)
    ms = rig.all_matrices(p)
    head = []
    stack = ["head-inside"]
    while stack:
        e = stack.pop()
        head += rig.corners(e, ms[e])
        stack += [c["name"] for c in rig.elements[e].get("children", [])]
    pen = 0.0
    radius = TRUNK_WIDTH / 2
    for c in head:
        x, y, z = kin.apply(inv, c)  # trunk frame: length along z, underside centre at the origin
        if abs(z) <= TRUNK_LENGTH / 2:
            d = math.hypot(x, y - radius)
            pen = max(pen, radius - d)
    out["trunk-carry: deepest head/hood corner inside a thin trunk (voxels)"] = round(pen, 2)
    tp = kin.apply(m, (0, 0, 0))
    out["trunk-carry: Trunk point (x, y, z)"] = tuple(round(v, 1) for v in tp)

    # Thick trunks: the hands on the near side, the trunk on the ground at the grab and release, and
    # clear of the body (the arms down to the wrists, the hands being where it is held).
    out["trunk-thick-carry-idle: hands to thick trunk grips, every frame"] = grip_err(
        "trunk-thick-carry-idle", range(60), THICK_GRIPS)
    out["trunk-thick-carry-walk: hands to thick trunk grips, every frame"] = grip_err(
        "trunk-thick-carry-walk", range(30), THICK_GRIPS)
    out["trunk-thick-pickup: hands to thick trunk grips, frames 22..59"] = grip_err(
        "trunk-thick-pickup", range(22, 60), THICK_GRIPS)
    out["trunk-thick-setdown: hands to thick trunk grips, frames 0..32"] = grip_err(
        "trunk-thick-setdown", range(0, 33), THICK_GRIPS)
    out["trunk-thick-pickup: thick trunk drift on the ground, frames 0..26"] = drift(
        "trunk-thick-pickup", "ThickTrunk", range(0, 27), THICK_GROUND)
    out["trunk-thick-setdown: thick trunk drift on the ground, frames 28..49"] = drift(
        "trunk-thick-setdown", "ThickTrunk", range(28, 50), THICK_GROUND)
    body, arms, head = thick_clearance(rig, b, anims)
    out["trunk-thick: deepest body point inside the thick trunk, every frame (voxels)"] = body
    out["trunk-thick: deepest arm point (not the hands) inside the thick trunk, every frame (voxels)"] = arms
    out["trunk-thick: nearest head or hood point to the thick trunk, every frame (voxels)"] = head
    for code, event in (("trunk-thick-pickup", "grab"), ("trunk-thick-setdown", "release")):
        p = kin.sample(anims[code], EVENTS[code][event])
        out[f"{code}: thick trunk underside height at the {event}"] = round(
            kin.apply(rig.attachment(*b.ap["ThickTrunk"], p), (0, 0, 0))[1], 2) + 0.0
    p = kin.sample(anims["trunk-thick-carry-idle"], 0)
    n, a = b.ap["ThickTrunk"]
    out["trunk-thick-carry: ThickTrunk point (x, y, z)"] = tuple(
        round(v, 1) for v in kin.apply(rig.attachment(n, a, p), (0, 0, 0)))
    return out


THICK_CODES = ("trunk-thick-pickup", "trunk-thick-carry-idle", "trunk-thick-carry-walk", "trunk-thick-setdown")


def thick_clearance(rig, b, anims):
    """Over every frame of the thick-trunk animations, how deep any sampled point of the body (every
    element but the arms and anchors), of the arms down to the wrists, goes inside the thick trunk's
    box, and how near the head and hood come to it (voxels). The box is the trunk's square
    collision section (TrunkBoxes), which is stricter than the rounder model."""
    arms, hands = set(), set()
    for root, wrist in (("upper-armR", "wristR"), ("upperarmL", "wristL")):
        stack = [root]
        while stack:
            e = stack.pop()
            (hands if e == wrist or wrist in rig.chain(e) else arms).add(e)
            stack += [c["name"] for c in rig.elements[e].get("children", [])]
    head = {e for e in rig.order if "head-inside" in rig.chain(e)} | {"hood-back3", "hood-back4"}
    half_w, len_2 = THICK_WIDTH / 2, THICK_LENGTH / 2
    n, a = b.ap["ThickTrunk"]

    def samples(e, m):
        el = rig.elements[e]
        size = [el["to"][i] - el["from"][i] for i in range(3)]
        return [kin.apply(m, (i * size[0] / 2, j * size[1] / 2, k * size[2] / 2))
                for i in range(3) for j in range(3) for k in range(3)]

    def depth(q):
        x, y, z = q
        return min(half_w - abs(x), y, THICK_WIDTH - y, len_2 - abs(z))

    def gap(q):
        x, y, z = q
        d = (max(abs(x) - half_w, 0.0), max(-y, y - THICK_WIDTH, 0.0), max(abs(z) - len_2, 0.0))
        return math.sqrt(sum(v * v for v in d))

    worst_body = worst_arm = 0.0
    nearest_head = float("inf")
    for code in THICK_CODES:
        for _, p in frame_poses(anims[code]):
            ms = rig.all_matrices(p)
            inv = kin.invert_rigid(rig.attachment(n, a, p))
            for e in rig.order:
                if e in hands or e in ANCHORS:
                    continue
                for q in samples(e, ms[e]):
                    local = kin.apply(inv, q)
                    d = depth(local)
                    if e in arms:
                        worst_arm = max(worst_arm, d)
                    else:
                        worst_body = max(worst_body, d)
                    if e in head:
                        nearest_head = min(nearest_head, gap(local))
    return round(worst_body, 2), round(worst_arm, 2), round(nearest_head, 2)


# ---------------------------------------------------------------------------------------------

def vanilla_path(game):
    game = game or os.environ.get("VINTAGE_STORY")
    if not game:
        return None
    p = Path(game) / VANILLA_REL
    return p if p.exists() else None


def load_vanilla(path):
    data = Path(path).read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    if digest != VANILLA_SHA256:
        raise SystemExit(f"{path}: sha256 {digest}, expected {VANILLA_SHA256} (game 1.22.7). "
                         "The snapshot is frozen; update VANILLA_SHA256 only on purpose.")
    return json.loads(data)


def main(argv=None):
    ap = argparse.ArgumentParser(description="Generate the eidolon's shape and build stages.")
    ap.add_argument("--game", help="a game install (default: $VINTAGE_STORY)")
    ap.add_argument("--check", action="store_true", help="compare with the committed files, write nothing")
    ap.add_argument("--report", action="store_true", help="print the pose checks of the committed shape")
    ap.add_argument("--vanilla-look", action="store_true", help="keep vanilla's texture map (no restyle)")
    args = ap.parse_args(argv)
    if args.report:
        shape = json.loads(SHAPE_OUT.read_text())
        for k, v in report(shape).items():
            print(f"{k}: {v}")
        return 0
    path = vanilla_path(args.game)
    if path is None:
        raise SystemExit("no game install: pass --game or set VINTAGE_STORY")
    shape = build(load_vanilla(path), vanilla_look=args.vanilla_look)
    stages = stages_file(shape)
    problems = check_stages(shape, stages)
    if problems:
        raise SystemExit("stage map: " + "; ".join(problems))
    files = {SHAPE_OUT: render(shape), STAGES_OUT: render(stages)}
    if args.check:
        stale = [str(p.relative_to(MOD)) for p, text in files.items() if not p.exists() or p.read_text() != text]
        if stale:
            print("stale: " + ", ".join(stale))
            return 1
        print("up to date")
        return 0
    for p, text in files.items():
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text)
        print(f"wrote {p.relative_to(MOD)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
