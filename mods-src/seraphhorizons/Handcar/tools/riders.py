"""The handcar's riders: the seraph's skeleton, its forward kinematics as the game computes them, and
the inverse kinematics that put both hands on the walking beam's handle at every frame of the cycle.

The player is the game's seraph (`game:shapes/entity/humanoid/seraph-faceless.json`). Its pose is
the shape's elements posed by animation keyframes, version 0 (the seraph's animations are all
version 0), which the game computes per element as

    L = T(rotationOrigin) . R(static rotation + keyframe rotation) . T(from - rotationOrigin + offset)

(`ShapeElement.GetLocalTransformMatrix`), children in their parent's frame (relative to its `from`),
R = Rx . Ry . Rz in degrees. A hand is its item anchor's attachment point (`RightHand` on
`ItemAnchor`, `LeftHand` on `ItemAnchorL`, both at the anchor's `from`), where the game hangs a held
item: the palm.

Two animations are written for the seraph, both `riderFrames` long, one frame per sixtieth of the
pump's cycle, and the gameplay sets their frame from the car's distance travelled, so both hands stay
on the handle at any speed:

    grip   the seat's animation: standing upright, both hands on the handle at the frame's phase
    pump   the effort: the torso leaning into each stroke, the head held level, and the arms solved
           again for that torso so the hands stay on the handle; eased in while the rider pumps

Everything here is in voxels, in the rider's model frame (the seraph faces -x, feet at (8, 0, 8)).
"""

from __future__ import annotations

import json
import math
import os
import re
from pathlib import Path

ARM_ELEMENTS = {"R": ("UpperArmR", "LowerArmR", "ItemAnchor", "RightHand"),
                "L": ("UpperArmL", "LowerArmL", "ItemAnchorL", "LeftHand")}
SOLVED = ("UpperArmR", "LowerArmR", "UpperArmL", "LowerArmL")

# The pump's effort: the torso leans forward (about the model's z) by this much at the bottom of the
# handle's stroke and back by as much at its top, the head turning back by the same so it stays level.
LEAN_DEG = 7.0

# A natural reaching pose to start from and to stay near (degrees, keyframe rotations added to the
# seraph's own): the upper arm forward and down, the forearm a little bent, the elbows out.
REST_GUESS = {
    "UpperArmR": [8.0, 0.0, -50.0], "LowerArmR": [-10.0, 0.0, -30.0],
    "UpperArmL": [-8.0, 0.0, -50.0], "LowerArmL": [10.0, 0.0, -30.0],
}
HAND_WEIGHT = 40.0                           # the hand's miss (voxels) against the pulls below: about 0.005 voxel left
REGULARISE = 2e-4                            # weight of staying near REST_GUESS, per degree squared
SMOOTH = 2e-3                                # weight of staying near the previous frame, per degree squared


def seraph_path():
    """The game's seraph shape, from the game install named by VINTAGE_STORY."""
    root = os.environ.get("VINTAGE_STORY")
    if not root:
        raise SystemExit("riders: set VINTAGE_STORY to the game (the seraph's shape is read from its assets)")
    p = Path(root) / "assets" / "game" / "shapes" / "entity" / "humanoid" / "seraph-faceless.json"
    if not p.exists():
        raise SystemExit(f"riders: no seraph shape at {p}")
    return p


def load_seraph(path=None):
    """The seraph's element tree, as {name: element} with parent links (the shape's own numbers)."""
    data = json.loads(Path(path or seraph_path()).read_text(encoding="utf-8-sig"))
    elements = {}

    def walk(els, parent):
        for e in els:
            elements[e["name"]] = {"from": e["from"], "to": e["to"],
                                   "origin": e.get("rotationOrigin", [0.0, 0.0, 0.0]),
                                   "rot": [e.get("rotationX", 0.0), e.get("rotationY", 0.0), e.get("rotationZ", 0.0)],
                                   "parent": parent,
                                   "aps": {a["code"]: [float(a["posX"]), float(a["posY"]), float(a["posZ"])]
                                           for a in e.get("attachmentpoints", [])}}
            walk(e.get("children", []), e["name"])
    walk(data["elements"], None)
    return elements


# ---------------------------------------------------------------- 4x4 maths (row-major, voxels)
def _mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def _t(v):
    return [[1, 0, 0, v[0]], [0, 1, 0, v[1]], [0, 0, 1, v[2]], [0, 0, 0, 1]]


def _r(axis, deg):
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    if axis == 0:
        m = [[1, 0, 0], [0, c, -s], [0, s, c]]
    elif axis == 1:
        m = [[c, 0, s], [0, 1, 0], [-s, 0, c]]
    else:
        m = [[c, -s, 0], [s, c, 0], [0, 0, 1]]
    return [m[0] + [0], m[1] + [0], m[2] + [0], [0, 0, 0, 1]]


def _rxyz(d):
    return _mul(_r(0, d[0]), _mul(_r(1, d[1]), _r(2, d[2])))


def _apply(m, p):
    return [m[i][0] * p[0] + m[i][1] * p[1] + m[i][2] * p[2] + m[i][3] for i in range(3)]


def local_matrix(e, deg=(0.0, 0.0, 0.0), offset=(0.0, 0.0, 0.0)):
    """The game's version 0 element transform, in voxels."""
    o, f = e["origin"], e["from"]
    rot = [e["rot"][k] + deg[k] for k in range(3)]
    return _mul(_t(o), _mul(_rxyz(rot), _t([f[k] - o[k] + offset[k] for k in range(3)])))


def world_matrix(skel, name, pose):
    """An element's matrix in the model frame, `pose` {element: (deg, offset)} the keyframe values."""
    chain = []
    n = name
    while n is not None:
        chain.append(n)
        n = skel[n]["parent"]
    m = [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]]
    for n in reversed(chain):
        deg, off = pose.get(n, ((0.0, 0.0, 0.0), (0.0, 0.0, 0.0)))
        m = _mul(m, local_matrix(skel[n], deg, off))
    return m


def hand(skel, side, pose):
    """Where a hand (its item anchor's attachment point) is, in the model frame."""
    _, _, anchor, ap = ARM_ELEMENTS[side]
    return _apply(world_matrix(skel, anchor, pose), skel[anchor]["aps"][ap])


# ---------------------------------------------------------------- inverse kinematics
def _solve_arm(skel, side, target, base_pose, start, ref):
    """Rotations of an arm's upper and lower element (6 numbers, degrees) putting its hand on
    `target`: damped Gauss-Newton on |hand - target|^2 plus small pulls towards `ref` and the
    starting guess (the previous frame), with a numerical Jacobian. Deterministic."""
    upper, lower = ARM_ELEMENTS[side][:2]
    x = list(start)
    prev = list(start)

    def pose_of(v):
        p = dict(base_pose)
        p[upper] = (tuple(v[0:3]), (0.0, 0.0, 0.0))
        p[lower] = (tuple(v[3:6]), (0.0, 0.0, 0.0))
        return p

    def residual(v):
        h = hand(skel, side, pose_of(v))
        r = [HAND_WEIGHT * (h[k] - target[k]) for k in range(3)]
        r += [math.sqrt(REGULARISE) * (v[i] - ref[i]) for i in range(6)]
        r += [math.sqrt(SMOOTH) * (v[i] - prev[i]) for i in range(6)]
        return r

    lam = 1e-3
    for _ in range(60):
        r0 = residual(x)
        cost0 = sum(v * v for v in r0)
        jac = []
        for i in range(6):
            xp = list(x)
            xp[i] += 1e-3
            ri = residual(xp)
            jac.append([(ri[k] - r0[k]) / 1e-3 for k in range(len(r0))])
        # normal equations (J^T J + lam I) dx = -J^T r
        a = [[sum(jac[i][k] * jac[j][k] for k in range(len(r0))) + (lam if i == j else 0.0) for j in range(6)] for i in range(6)]
        g = [-sum(jac[i][k] * r0[k] for k in range(len(r0))) for i in range(6)]
        dx = _gauss(a, g)
        trial = [x[i] + dx[i] for i in range(6)]
        cost1 = sum(v * v for v in residual(trial))
        if cost1 < cost0:
            x = trial
            lam = max(lam / 3, 1e-6)
            if cost0 - cost1 < 1e-12:
                break
        else:
            lam *= 4
    return x


def _gauss(a, b):
    n = len(b)
    m = [row[:] + [b[i]] for i, row in enumerate(a)]
    for c in range(n):
        p = max(range(c, n), key=lambda r: abs(m[r][c]))
        m[c], m[p] = m[p], m[c]
        for r in range(n):
            if r != c:
                f = m[r][c] / m[c][c]
                for k in range(c, n + 1):
                    m[r][k] -= f * m[c][k]
    return [m[i][n] / m[i][i] for i in range(n)]


def body_pose(lean_deg):
    """The pose of everything but the arms: the torso leaning `lean_deg` forward (about +z: the
    seraph faces -x, so a positive turn takes its head forward), the head turned back as much."""
    return {"UpperTorso": ((0.0, 0.0, lean_deg), (0.0, 0.0, 0.0)),
            "Neck": ((0.0, 0.0, -lean_deg * 0.5), (0.0, 0.0, 0.0)),
            "Head": ((0.0, 0.0, -lean_deg * 0.5), (0.0, 0.0, 0.0))}


def solve_cycle(skel, targets, leans):
    """Both arms over a cycle. `targets[i]` is {"R": point, "L": point} at frame i, `leans[i]` the
    torso's lean there. Returns per frame the full pose (degrees and offsets per element)."""
    frames = []
    start = {"R": REST_GUESS["UpperArmR"] + REST_GUESS["LowerArmR"],
             "L": REST_GUESS["UpperArmL"] + REST_GUESS["LowerArmL"]}
    ref = dict(start)
    # Two passes, so frame 0 starts from where the cycle ends: the loop closes smoothly.
    for sweep in range(2):
        frames = []
        for i, tgt in enumerate(targets):
            base = body_pose(leans[i])
            pose = dict(base)
            for side in ("R", "L"):
                upper, lower = ARM_ELEMENTS[side][:2]
                v = _solve_arm(skel, side, tgt[side], base, start[side], ref[side])
                start[side] = v
                pose[upper] = (tuple(v[0:3]), (0.0, 0.0, 0.0))
                pose[lower] = (tuple(v[3:6]), (0.0, 0.0, 0.0))
            frames.append(pose)
    return frames


def lerp_pose(a, b, t):
    """The pose between two frames, as the game interpolates keyframes: linearly, value by value."""
    out = {}
    for name in set(a) | set(b):
        da, oa = a.get(name, ((0.0,) * 3, (0.0,) * 3))
        db, ob = b.get(name, ((0.0,) * 3, (0.0,) * 3))
        out[name] = (tuple(da[k] + (db[k] - da[k]) * t for k in range(3)), tuple(oa[k] + (ob[k] - oa[k]) * t for k in range(3)))
    return out


# ---------------------------------------------------------------- the animations as the shape stores them
ANIMATED = ("LowerTorso", "UpperTorso", "Neck", "Head", "UpperArmR", "LowerArmR", "UpperArmL", "LowerArmL",
            "UpperFootR", "LowerFootR", "UpperFootL", "LowerFootL")


def r4(x, n=4):
    v = round(x, n)
    return 0.0 if v == 0 else v


def animation_json(code, name, frames):
    """A seraph animation: a keyframe at every frame for every element of the body (the legs and the
    lower torso held at the seraph's own standing pose), so no other animation's pose shows through
    where it weighs less."""
    keyframes = []
    for i, pose in enumerate(frames):
        els = {}
        for n in ANIMATED:
            deg, off = pose.get(n, ((0.0, 0.0, 0.0), (0.0, 0.0, 0.0)))
            els[n] = {"offsetX": r4(off[0]), "offsetY": r4(off[1]), "offsetZ": r4(off[2]),
                      "rotationX": r4(deg[0]), "rotationY": r4(deg[1]), "rotationZ": r4(deg[2])}
        keyframes.append({"frame": i, "elements": els})
    return {"name": name, "code": code, "quantityframes": len(frames),
            "onActivityStopped": "EaseOut", "onAnimationEnd": "Repeat", "keyframes": keyframes}


def pose_from_json(anim, frame):
    """A keyframe of a written animation back as a pose (what the game reads)."""
    kf = anim["keyframes"][frame]["elements"]
    return {n: ((v["rotationX"], v["rotationY"], v["rotationZ"]), (v["offsetX"], v["offsetY"], v["offsetZ"])) for n, v in kf.items()}


def worst_hand_error(skel, anim, targets_at, samples):
    """The worst distance (voxels) between a hand and its target over `samples` (fractional frames),
    the pose interpolated between keyframes as the game does. `targets_at(f)` gives the targets at
    fractional frame f."""
    n = len(anim["keyframes"])
    worst = 0.0
    for f in samples:
        i = int(math.floor(f)) % n
        t = f - math.floor(f)
        pose = lerp_pose(pose_from_json(anim, i), pose_from_json(anim, (i + 1) % n), t)
        tg = targets_at(f)
        for side in ("R", "L"):
            h = hand(skel, side, pose)
            worst = max(worst, math.dist(h, tg[side]))
    return worst


SKELETON_KEYS = ("LowerTorso", "UpperTorso", "UpperArmR", "LowerArmR", "ItemAnchor", "UpperArmL", "LowerArmL", "ItemAnchorL", "Neck", "Head")


def skeleton_summary(skel):
    """The numbers of the seraph the solution depends on, for the reference file: a game update that
    moves them fails the tests until the animations are generated again."""
    return {n: {"from": skel[n]["from"], "origin": skel[n]["origin"], "rot": skel[n]["rot"]} for n in SKELETON_KEYS}


def dumps_patch(ops):
    """The riders' patch file, one animation keyframe per line."""
    out = ["["]
    for i, op in enumerate(ops):
        end = "," if i < len(ops) - 1 else ""
        value = op["value"]
        if isinstance(value, dict) and "keyframes" in value:
            head = {k: v for k, v in value.items() if k != "keyframes"}
            lines = [f'\t\t{json.dumps(k)}: {json.dumps(v)},' for k, v in head.items()]
            kfs = ",\n".join("\t\t\t" + json.dumps(kf, separators=(",", ":")) for kf in value["keyframes"])
            body = "\n".join(lines) + '\n\t\t"keyframes": [\n' + kfs + "\n\t\t]"
            meta = {k: v for k, v in op.items() if k != "value"}
            out.append("\t{")
            for k, v in meta.items():
                out.append(f"\t\t{json.dumps(k)}: {json.dumps(v)},")
            out.append('\t\t"value": {\n' + re.sub(r"^\t\t", "\t\t\t", body, flags=re.M) + "\n\t\t}")
            out.append("\t}" + end)
        else:
            out.append("\t" + json.dumps(op, separators=(", ", ": ")) + end)
    out.append("]")
    return "\n".join(out) + "\n"
