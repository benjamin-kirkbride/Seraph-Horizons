"""The rig's driver maths: the reference implementation every other one is held to.

`Machines/Core/RigAnimation.cs` (the game's renderer) and `site/src/lib/rig.ts` (the browser viewer)
implement exactly this. The bucking sawmill's generator writes its reference poses from it, and
`Machines/tools/make_fixture.py` writes `tests/Machines/driver-fixture.json` from it.

Inputs (a dict; every key is optional, see `full_inputs`):

    theta     the signed shaft angle, radians
    depth     the mill's saw depth, 0 (at the top) .. 1 (at the bed)
    lifting   1 while the mill's saws are wound back up, else 0 (a renderer may ease it)
    travel    psi, the shaft's travel: the total angle it has turned through either way (radians,
              never decreasing). Missing or None means |theta|.
    work      W, the machine's work: how far its job has got, in the unit its rig's progress names
              (0 .. end(size)). "trunk" is the same input under its trunk-flavoured name (a trunk's
              travel along the rig's trunkPath, blocks); a dict may give either key.
    size      k, the work's class: 0 none, 1 thin, 2 thick (a trunk's class; the gear cutter's master)
    presence  p, 0..1: how far the work (a loaded trunk, a fitted master) has eased in (renderer-side)
    feed      phi, radians: the shaft's travel while the feed runs (renderer-side, never decreasing)
    oil       0..1, how full the machine's oil tank is (MachineOil; the gear cutter's sight-feed cup)

The rig's progress (what gauges and rolls read; `progress_of`) is one of two things:

    work       {"name", "unit", "step"?, "end": {"thin", "thick"}}: a named quantity, W in its unit,
               running 0 .. end[k]. It is a point: nose(W) = tail(W) = W, so a gauge window is
               occupied while from <= W <= to (eased at both ends).
    trunkPath  the trunk-flavoured case, a trunk travelling along a line: nose(W) = nose0 + W and
               tail(W, k) = nose - L_k, with L = [0, lengths.thin, lengths.thick]; it ends when the
               tail reaches tailStop. Its unit is blocks and its step 1/16.

A rig has at most one of them.

Matrices are 4x4 lists, block units; rotations are right-handed about the positive axis. A part's
matrix is its drivers composed in list order (each applied to the authored geometry, pivots in the
authored frame), then its `ride` part's whole matrix on top.

Drivers (design: build/rosser/design.md section 2.3; the mill's README has the old ones):

    rotate   angle = ratio * x                                   about pivot
    swing    angle = amplitude * sin(ratio * x + phase)          about pivot
    slide    offset = amplitude * sin(ratio * x + phase)         along axis
             x is the driver's input: "input" one of "theta" (the default), "travel" (psi), "feed"
             (phi), "work" (W; "trunk" is the same) or "oil". "rectified": true is the mill's spelling
             of "input": "travel"; a driver with both keys is an error.
    feed     offset = travel * depth                             along axis
    step     a ramp of depth over [from, to] with lift gates hold / block / trip, times amount
    stretch  scales along axis about anchor: (length + travel * x) / length, x its "input": "depth"
             (the default) or "oil"
    gauge    e = 0 if k == 0; p if mode == "present"; else
                 p * max over windows w of min(1, gain_w[k] * occ_w),
                 occ_w = clamp((nose - from_w) / ease_w, 0, 1) * clamp((to_w - tail) / ease_w, 0, 1)
             a = amount[k] * e, plus e * lobes.amplitude[k] * cos(lobes.ratio * psi + lobes.phase)
             motion "slide": along axis by a; "rotate": a radians about pivot
    roll     identity if k == 0, else ratio * clamp(nose - at, 0, L_k) radians about pivot (a
             trunkPath only: a work quantity has no length to roll over)
"""

from __future__ import annotations

import math
import re

from .geometry import IDENT, El, mmul, mvec, rot

AXES = {"x": 0, "y": 1, "z": 2}
CLASSES = ("none", "thin", "thick")          # k = 0, 1, 2; the per-class JSON keys are CLASSES[1:]
INPUTS = ("theta", "travel", "feed", "work", "trunk", "oil")
INPUT_ALIASES = {"trunk": "work"}            # the trunk-flavoured spelling of the work input
STRETCH_INPUTS = ("depth", "oil")
INPUT_KEYS = ("theta", "depth", "lifting", "travel", "work", "size", "presence", "feed", "oil")
DRIVER_TYPES = ("rotate", "swing", "slide", "feed", "step", "stretch", "gauge", "roll")


# ---------------------------------------------------------------- 4x4 helpers
def m4(r=IDENT, t=(0.0, 0.0, 0.0)):
    return [[r[0][0], r[0][1], r[0][2], t[0]], [r[1][0], r[1][1], r[1][2], t[1]], [r[2][0], r[2][1], r[2][2], t[2]], [0, 0, 0, 1]]


def m4mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def about(r, pivot):
    """The 3x3 rotation `r` about `pivot`, as a 4x4."""
    t = [pivot[i] - mvec(r, pivot)[i] for i in range(3)]
    return m4(r, t)


def apply(m, p):
    """A point moved by a 4x4."""
    return [m[i][0] * p[0] + m[i][1] * p[1] + m[i][2] * p[2] + m[i][3] for i in range(3)]


# ---------------------------------------------------------------- inputs and the rig's progress
def full_inputs(inputs):
    """Every input key, with its default: 0, except travel, which defaults to |theta|. W may be
    given as "work" or as "trunk"."""
    out = {k: inputs.get(k, 0.0) for k in INPUT_KEYS}
    if "work" not in inputs and "trunk" in inputs:
        out["work"] = inputs["trunk"]
    out["size"] = int(inputs.get("size", 0))
    if inputs.get("travel") is None:
        out["travel"] = abs(out["theta"])
    return out


def is_trunk_path(path) -> bool:
    """A trunkPath (a trunk with a length on a line), as against a plain work quantity."""
    return "lengths" in path


def validate_work(work):
    """Raises ValueError unless `work` is a sound rig `work` quantity."""
    if not isinstance(work, dict):
        raise ValueError("work must be an object")
    if not isinstance(work.get("unit"), str) or not work["unit"]:
        raise ValueError("work needs a unit")
    if "name" in work and (not isinstance(work["name"], str) or not work["name"]):
        raise ValueError("work.name must be a non-empty string")
    if "step" in work and not (isinstance(work["step"], (int, float)) and work["step"] > 0):
        raise ValueError("work.step must be above 0")
    end = work.get("end")
    if not isinstance(end, dict) or not all(isinstance(end.get(c), (int, float)) and end[c] > 0 for c in CLASSES[1:]):
        raise ValueError("work.end needs thin and thick above 0")
    for key in ("nose0", "lengths", "tailStop"):
        if key in work:
            raise ValueError(f"work has no {key}: that is a trunkPath's")
    return work


def progress_of(rig):
    """The rig's progress, which gauges and rolls read: its `work`, its `trunkPath`, or None."""
    if "work" in rig and "trunkPath" in rig:
        raise ValueError("a rig has work or a trunkPath, not both")
    if "work" in rig:
        return validate_work(rig["work"])
    return rig.get("trunkPath")


def trunk_length(path, k: int) -> float:
    """L_k: 0 for no class or a work quantity, else the trunk's length for that class (blocks)."""
    return 0.0 if k == 0 or not is_trunk_path(path) else float(path["lengths"][CLASSES[k]])


def nose(path, work: float) -> float:
    return path.get("nose0", 0.0) + work


def tail(path, work: float, k: int) -> float:
    return nose(path, work) - trunk_length(path, k)


def work_end(path, k: int) -> float:
    """end(k): a work quantity's end for the class; a trunk's travel when its tail reaches tailStop."""
    if not is_trunk_path(path):
        return 0.0 if k == 0 else float(path["end"][CLASSES[k]])
    return path["tailStop"] + trunk_length(path, k) - path["nose0"]


trunk_end = work_end                         # the trunk-flavoured name


def _clamp01(x):
    return min(1.0, max(0.0, x))


# ---------------------------------------------------------------- parsing rules
def driver_input(d) -> str:
    """The input a rotate, swing or slide driver reads (raises on a bad or conflicting key)."""
    if "input" in d:
        if "rectified" in d:
            raise ValueError(f"{d['type']} driver has both 'input' and 'rectified'")
        if d["input"] not in INPUTS:
            raise ValueError(f"unknown input {d['input']!r}")
        return INPUT_ALIASES.get(d["input"], d["input"])
    return "travel" if d.get("rectified") else "theta"


def _per_class(v, what, default=None):
    if not isinstance(v, dict) or (default is None and not all(c in v for c in CLASSES[1:])):
        raise ValueError(f"{what} needs both 'thin' and 'thick'")
    return [0.0] + [float(v.get(c, default)) for c in CLASSES[1:]]


def validate_driver(d):
    """Raises ValueError if the driver breaks a parse rule every implementation enforces."""
    kind = d.get("type")
    if kind not in DRIVER_TYPES:
        raise ValueError(f"unknown driver type {kind!r}")
    if d.get("axis") not in AXES:
        raise ValueError(f"{kind} driver has no axis x, y or z")
    if kind in ("rotate", "swing", "slide"):
        driver_input(d)
    if kind == "stretch" and d.get("input", "depth") not in STRETCH_INPUTS:
        raise ValueError(f"unknown stretch input {d['input']!r}")
    if kind == "gauge":
        if d.get("motion") not in ("slide", "rotate"):
            raise ValueError("gauge motion must be 'slide' or 'rotate'")
        if d["motion"] == "rotate" and "pivot" not in d:
            raise ValueError("a rotating gauge needs a pivot")
        mode = d.get("mode", "occupy")
        if mode not in ("occupy", "present"):
            raise ValueError(f"unknown gauge mode {mode!r}")
        _per_class(d.get("amount"), "gauge amount")
        if mode == "occupy" and not d.get("windows"):
            raise ValueError("an occupy gauge needs windows")
        for w in d.get("windows", []) if mode == "occupy" else []:
            if not w.get("ease", 0) > 0:
                raise ValueError("a gauge window's ease must be > 0")
            if not w["to"] > w["from"]:
                raise ValueError("a gauge window's 'to' must be past its 'from'")
            if "gain" in w:
                _per_class(w["gain"], "gauge gain", default=1.0)
        if "lobes" in d:
            if d["motion"] == "slide":
                raise ValueError("lobes on a sliding gauge")
            if "ratio" not in d["lobes"]:
                raise ValueError("gauge lobes need a ratio")
            _per_class(d["lobes"].get("amplitude"), "gauge lobes' amplitude")
    if kind == "roll":
        for key in ("pivot", "at", "ratio"):
            if key not in d:
                raise ValueError(f"a roll driver needs {key!r}")
    return d


# ---------------------------------------------------------------- drivers
def step_amount(d, depth, lifting):
    """A step driver's fraction e in [0, 1]: the depth's progress through [from, to], then gated:
    "hold" keeps it at 1 while lifting, "block" keeps it at 0 while lifting."""
    lo, hi = d.get("from", 0.0), d.get("to", 1.0)
    e = min(1.0, max(0.0, (depth - lo) / (hi - lo)))
    gate = d.get("lifting")
    if gate == "hold":
        e = max(e, lifting)
    elif gate == "block":
        e = e * (1.0 - lifting)
    elif gate == "trip":
        # thrown over [from, to] at the bottom on the way down, back over [0, top] at the top on
        # the way up; the two halves agree at both ends, where the direction changes
        e = e * (1.0 - lifting) + min(1.0, max(0.0, depth / d["top"])) * lifting
    return e


def gauge_fraction(d, inputs, path):
    """A gauge's e in [0, 1] (inputs as from `full_inputs`)."""
    k = inputs["size"]
    if k == 0:
        return 0.0
    if d.get("mode", "occupy") == "present":
        return inputs["presence"]
    if path is None:
        raise ValueError("an occupy gauge needs the rig's work or trunkPath")
    n, t = nose(path, inputs["work"]), tail(path, inputs["work"], k)
    best = 0.0
    for w in d["windows"]:
        occ = _clamp01((n - w["from"]) / w["ease"]) * _clamp01((w["to"] - t) / w["ease"])
        gain = w.get("gain", {}).get(CLASSES[k], 1.0)
        best = max(best, min(1.0, gain * occ))
    return inputs["presence"] * best


def gauge_amount(d, inputs, path):
    """A gauge's motion: blocks for a slide, radians for a rotate."""
    e = gauge_fraction(d, inputs, path)
    k = inputs["size"]
    a = d["amount"][CLASSES[k]] * e if k else 0.0
    lobes = d.get("lobes")
    if lobes and k:
        a += e * lobes["amplitude"][CLASSES[k]] * math.cos(lobes["ratio"] * inputs["travel"] + lobes.get("phase", 0.0))
    return a


def roll_angle(d, inputs, path):
    k = inputs["size"]
    if k == 0:
        return 0.0
    if path is None or not is_trunk_path(path):
        raise ValueError("a roll driver needs the rig's trunkPath")
    over = min(trunk_length(path, k), max(0.0, nose(path, inputs["work"]) - d["at"]))

    return d["ratio"] * over


def driver_matrix(d, inputs, path=None):
    """One driver's 4x4 at `inputs` (a dict, see the module's docstring); `path` is the rig's
    progress (`progress_of`: its work or trunkPath), needed by occupy gauges and rolls."""
    inputs = full_inputs(inputs)
    axis = d["axis"]
    unit = [0.0, 0.0, 0.0]
    unit[AXES[axis]] = 1.0
    kind = d["type"]
    if kind == "rotate":
        return about(rot(axis, math.degrees(d.get("ratio", 1.0) * inputs[driver_input(d)])), d["pivot"])
    if kind == "swing":
        ang = d["amplitude"] * math.sin(d.get("ratio", 1.0) * inputs[driver_input(d)] + d.get("phase", 0.0))
        return about(rot(axis, math.degrees(ang)), d["pivot"])
    if kind == "slide":
        x = inputs[driver_input(d)]
        return m4(IDENT, [u * d["amplitude"] * math.sin(d.get("ratio", 1.0) * x + d.get("phase", 0.0)) for u in unit])
    if kind == "feed":
        return m4(IDENT, [u * d["travel"] * inputs["depth"] for u in unit])
    if kind == "step":
        e = step_amount(d, inputs["depth"], inputs["lifting"])
        if d["motion"] == "rotate":
            return about(rot(axis, math.degrees(d["amount"] * e)), d["pivot"])
        return m4(IDENT, [u * d["amount"] * e for u in unit])
    if kind == "stretch":
        k = AXES[axis]
        f = (d["length"] + d["travel"] * inputs[d.get("input", "depth")]) / d["length"]
        m = m4()
        m[k][k] = f
        m[k][3] = d["anchor"][k] * (1.0 - f)
        return m
    if kind == "gauge":
        a = gauge_amount(d, inputs, path)
        if d["motion"] == "rotate":
            return about(rot(axis, math.degrees(a)), d["pivot"])
        return m4(IDENT, [u * a for u in unit])
    if kind == "roll":
        return about(rot(axis, math.degrees(roll_angle(d, inputs, path))), d["pivot"])
    raise ValueError(kind)


def part_matrix(parts, pid, inputs, path=None):
    """Drivers apply in list order to the authored geometry (pivots in the authored frame);
    then the `ride` part's whole transform is applied on top."""
    inputs = full_inputs(inputs)
    p = next(q for q in parts if q["id"] == pid)
    m = m4()
    for d in p["drivers"]:
        m = m4mul(driver_matrix(d, inputs, path), m)
    if p.get("ride"):
        m = m4mul(part_matrix(parts, p["ride"], inputs, path), m)
    return m


# The mill's four-input signatures, kept so its generator's calls stay as they were.
def driver_matrix_mill(d, theta, depth, lifting=0.0, travel=None):
    return driver_matrix(d, {"theta": theta, "depth": depth, "lifting": lifting, "travel": travel})


def part_matrix_mill(parts, pid, theta, depth, lifting=0.0, travel=None):
    return part_matrix(parts, pid, {"theta": theta, "depth": depth, "lifting": lifting, "travel": travel})


# ---------------------------------------------------------------- posing elements, finding parts
def posed(el: El, m) -> El:
    """`el` (voxels) moved by a part matrix (blocks). A stretch scales the element's extent along
    its axis (the rope is axis-aligned, so this stays a box)."""
    out = el.clone()
    lin = [row[:3] for row in m[:3]]
    t = [m[i][3] * 16 for i in range(3)]
    scale = [math.sqrt(sum(lin[i][j] ** 2 for i in range(3))) for j in range(3)]
    r = [[lin[i][j] / scale[j] for j in range(3)] for i in range(3)]
    out.c = [mvec(lin, el.c)[i] + t[i] for i in range(3)]
    if any(abs(s - 1) > 1e-9 for s in scale):
        for k in range(3):                       # world axis j scaled: stretch the local axis along it
            ax = [abs(el.r[j][k]) for j in range(3)]
            out.size[k] = el.size[k] * sum(ax[j] * scale[j] for j in range(3))
    out.r = mmul(r, el.r)
    return out


def glob_rx(pattern):
    return re.compile("^" + re.escape(pattern).replace(r"\*", ".*") + "$")


def part_of(parts, name):
    """The first part whose `match` globs take this element name, or None."""
    for p in parts:
        if any(glob_rx(g).match(name) for g in p["match"]):
            return p["id"]
    return None
