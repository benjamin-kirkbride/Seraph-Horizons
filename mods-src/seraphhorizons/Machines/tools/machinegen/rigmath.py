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
    size      k, the work's class: 0 none, 1 thin, 2 thick (a trunk's class; the gear cutter's master),
              3 .. MAX_CLASS further classes (the mandrel station's spring is 3)
    presence  p, 0..1: how far the work (a loaded trunk, a fitted master) has eased in (renderer-side)
    feed      phi, radians: the shaft's travel while the feed runs (renderer-side, never decreasing)
    oil       0..1, how full the machine's oil tank is (MachineOil; the gear cutter's sight-feed cup)

Classes. k is 0 (none) or a class 1 .. MAX_CLASS. A per-class value in a rig (a gauge's `amount`, a
window's `gain`, lobes' `amplitude`, a work's `end`) is an object keyed by class: "thin" (1), "thick"
(2), and from 3 on the class's number ("3" .. "9"); any other key is refused. It names one class or
more (a `gain` may name none), and a class it does not name has none of it: amount and amplitude 0,
gain 1, a work's end 0 (no work of that class). So a part that moves only for class 3 gives its
amount for "3" alone, and stands still for 1 and 2. A class outside 1 .. MAX_CLASS is none.

The rig's progress (what gauges and rolls read; `progress_of`) is one of two things:

    work       {"name", "unit", "step"?, "end": {class: end, ...}}: a named quantity, W in its unit,
               running 0 .. end[k]. It is a point: nose(W) = tail(W) = W, so a gauge window is
               occupied while from <= W <= to (eased at both ends).
    trunkPath  the trunk-flavoured case, a trunk travelling along a line: nose(W) = nose0 + W and
               tail(W, k) = nose - L_k, with L = [0, lengths.thin, lengths.thick] (a trunk has the two
               classes; L is 0 for any other); it ends when the tail reaches tailStop. Its unit is
               blocks and its step 1/16.

A rig has at most one of them.

Matrices are 4x4 lists, block units; rotations are right-handed about the positive axis. A part's
matrix is its drivers composed in list order (each applied to the authored geometry, pivots in the
authored frame), then its `ride` part's rigid matrix on top: the ride part's matrix without its
scales (its `stretch` drivers and stretch gauges, `is_scale`), so a part riding a stretched one moves
and turns with it but is never stretched by it. A part without scales is its own rigid matrix.

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
    gauge    e = 0 if k is none; p if mode == "present"; else
                 p * max over windows w of min(1, gain_w[k] * occ_w),
                 occ_w = clamp((nose - from_w) / ease_w, 0, 1) * clamp((to_w - tail) / ease_w, 0, 1)
             a = amount[k] * e, plus e * lobes.amplitude[k] * cos(lobes.ratio * psi + lobes.phase)
             motion "slide": along axis by a; "rotate": a radians about pivot
             motion "stretch": a scale f = 1 + amount[k] * E along one of its own axes about `origin`,
                 E = p * sum over windows w of min(1, gain_w[k] * occ_w) (a sum, not the most engaged
                 window, so windows add up: a stretch done in steps), or p when "present"; `axis` is
                 x, y or z of the frame `rotation` [rx, ry, rz] (degrees, Rx * Ry * Rz, an element's
                 rotationX/Y/Z; none is the authored frame). f is never below 0, and under STRETCH_EPS
                 it is 0: the part collapses to `origin`, drawn as nothing (a vanished part).
    roll     identity if k == 0, else ratio * clamp(nose - at, 0, L_k) radians about pivot (a
             trunkPath only: a work quantity has no length to roll over)
"""

from __future__ import annotations

import math
import re

from .geometry import IDENT, El, from_euler, mmul, mvec, rot

AXES = {"x": 0, "y": 1, "z": 2}
CLASSES = ("none", "thin", "thick")          # k = 0, 1, 2 by name; the named per-class JSON keys are CLASSES[1:]
MAX_CLASS = 9                                # classes 3 .. MAX_CLASS are keyed by their number, "3" .. "9"
INPUTS = ("theta", "travel", "feed", "work", "trunk", "oil")
INPUT_ALIASES = {"trunk": "work"}            # the trunk-flavoured spelling of the work input
STRETCH_INPUTS = ("depth", "oil")
INPUT_KEYS = ("theta", "depth", "lifting", "travel", "work", "size", "presence", "feed", "oil")
DRIVER_TYPES = ("rotate", "swing", "slide", "feed", "step", "stretch", "gauge", "roll")
GAUGE_MOTIONS = ("slide", "rotate", "stretch")
STRETCH_EPS = 1e-6                           # a stretch gauge's factor below this is 0: the part vanishes


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
def class_key(k):
    """Class k's per-class JSON key: "thin", "thick", "3" .. "9"; None for none or a class out of range."""
    k = int(k)
    if k < 1 or k > MAX_CLASS:
        return None
    return CLASSES[k] if k < len(CLASSES) else str(k)


def class_of_key(key):
    """The class a per-class JSON key names (1 .. MAX_CLASS), or None for any other key."""
    if key in CLASSES[1:]:
        return CLASSES.index(key)
    if isinstance(key, str) and key.isdigit() and str(int(key)) == key and len(CLASSES) <= int(key) <= MAX_CLASS:
        return int(key)
    return None


def per_class(v, k, missing=0.0):
    """Class k's value in a per-class object, `missing` when it names no value for k (or k is none)."""
    key = class_key(k)
    if key is None or not isinstance(v, dict) or key not in v:
        return missing
    return float(v[key])


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
    end = _per_class(work.get("end"), "work.end")
    if not all(v > 0 for v in end.values()):
        raise ValueError("work.end must be above 0 for every class it names")
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
    """L_k: 0 for no class or a work quantity, else the trunk's length for that class (blocks; 0 for a
    class a trunk does not have)."""
    return 0.0 if not is_trunk_path(path) else per_class(path["lengths"], k)


def nose(path, work: float) -> float:
    return path.get("nose0", 0.0) + work


def tail(path, work: float, k: int) -> float:
    return nose(path, work) - trunk_length(path, k)


def work_end(path, k: int) -> float:
    """end(k): a work quantity's end for the class (0 for none, or a class it has no end for); a trunk's
    travel when its tail reaches tailStop."""
    if not is_trunk_path(path):
        return per_class(path["end"], k)
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


def _per_class(v, what, empty=False):
    """A per-class object checked: keyed by class ("thin", "thick", "3" .. "9"), each value a number, naming one
    class or more (none at all when `empty`). Returns {class: value}."""
    if not isinstance(v, dict):
        raise ValueError(f"{what} must be an object keyed by class")
    out = {}
    for key, val in v.items():
        k = class_of_key(key)
        if k is None:
            raise ValueError(f"{what}: {key!r} is not a class (thin, thick, or 3 .. {MAX_CLASS})")
        if isinstance(val, bool) or not isinstance(val, (int, float)):
            raise ValueError(f"{what}.{key} must be a number")
        out[k] = float(val)
    if not out and not empty:
        raise ValueError(f"{what} names no class")
    return out


def validate_driver(d):
    """Raises ValueError if the driver breaks a parse rule every implementation enforces."""
    kind = d.get("type")
    if kind not in DRIVER_TYPES:
        raise ValueError(f"unknown driver type {kind!r}")
    if d.get("axis") not in AXES:
        raise ValueError(f"{kind} driver has no axis x, y or z")
    if kind in ("rotate", "swing", "slide"):
        driver_input(d)
    if kind == "step" and d.get("motion") not in ("slide", "rotate"):
        raise ValueError("step motion must be 'slide' or 'rotate'")
    if kind == "stretch" and d.get("input", "depth") not in STRETCH_INPUTS:
        raise ValueError(f"unknown stretch input {d['input']!r}")
    if kind == "gauge":
        if d.get("motion") not in GAUGE_MOTIONS:
            raise ValueError("gauge motion must be 'slide', 'rotate' or 'stretch'")
        if d["motion"] == "rotate" and "pivot" not in d:
            raise ValueError("a rotating gauge needs a pivot")
        if d["motion"] == "stretch":
            if not _is_vec3(d.get("origin")):
                raise ValueError("a stretching gauge needs an origin of 3 numbers")
            if "rotation" in d and not _is_vec3(d["rotation"]):
                raise ValueError("a stretching gauge's rotation must be 3 numbers (degrees)")
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
                _per_class(w["gain"], "gauge gain", empty=True)
        if "lobes" in d:
            if d["motion"] != "rotate":
                raise ValueError(f"lobes on a {d['motion']} gauge")
            if "ratio" not in d["lobes"]:
                raise ValueError("gauge lobes need a ratio")
            _per_class(d["lobes"].get("amplitude"), "gauge lobes' amplitude")
    if kind == "roll":
        for key in ("pivot", "at", "ratio"):
            if key not in d:
                raise ValueError(f"a roll driver needs {key!r}")
    return d


def _is_vec3(v):
    return (isinstance(v, list) and len(v) == 3
            and all(isinstance(x, (int, float)) and not isinstance(x, bool) for x in v))


def is_scale(d) -> bool:
    """A driver that scales its part (a `stretch`, a stretching gauge): what a rider does not inherit."""
    return d["type"] == "stretch" or (d["type"] == "gauge" and d.get("motion") == "stretch")


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
    if class_key(k) is None:
        return 0.0
    if d.get("mode", "occupy") == "present":
        return inputs["presence"]
    if path is None:
        raise ValueError("an occupy gauge needs the rig's work or trunkPath")
    n, t = nose(path, inputs["work"]), tail(path, inputs["work"], k)
    best = 0.0
    total = 0.0
    for w in d["windows"]:
        occ = _clamp01((n - w["from"]) / w["ease"]) * _clamp01((w["to"] - t) / w["ease"])
        gain = per_class(w.get("gain", {}), k, 1.0)
        best = max(best, min(1.0, gain * occ))
        total += min(1.0, gain * occ)
    # a stretch sums its windows (each at most 1), so a stretch done in steps adds up; the others take the most engaged
    return inputs["presence"] * (total if d.get("motion") == "stretch" else best)


def gauge_amount(d, inputs, path):
    """A gauge's motion: blocks for a slide, radians for a rotate (0 for a class its amount does not name)."""
    e = gauge_fraction(d, inputs, path)
    k = inputs["size"]
    a = per_class(d["amount"], k) * e
    lobes = d.get("lobes")
    if lobes and class_key(k) is not None:
        a += e * per_class(lobes["amplitude"], k) * math.cos(lobes["ratio"] * inputs["travel"] + lobes.get("phase", 0.0))
    return a


def stretch_factor(d, inputs, path):
    """A stretching gauge's scale f: 1 + amount[k] * E, never below 0, and 0 under STRETCH_EPS."""
    f = 1.0 + gauge_amount(d, inputs, path)
    return 0.0 if f < STRETCH_EPS else f


def stretch_direction(d):
    """The unit axis a stretching gauge scales along, in the authored frame: its `axis` of the frame
    `rotation` turns to (Rx * Ry * Rz, degrees), or of the authored frame without one."""
    r = from_euler(*d["rotation"]) if "rotation" in d else IDENT
    k = AXES[d["axis"]]
    return [r[i][k] for i in range(3)]


def scale_along(u, f, origin):
    """A scale by f along the unit vector u through `origin` (the plane through it normal to u stays put);
    f == 0 collapses everything to `origin`."""
    if f == 0.0:
        return [[0.0, 0.0, 0.0, origin[0]], [0.0, 0.0, 0.0, origin[1]], [0.0, 0.0, 0.0, origin[2]], [0, 0, 0, 1]]
    lin = [[(1.0 if i == j else 0.0) + (f - 1.0) * u[i] * u[j] for j in range(3)] for i in range(3)]
    return m4(lin, [origin[i] - mvec(lin, origin)[i] for i in range(3)])


def roll_angle(d, inputs, path):
    k = inputs["size"]
    if class_key(k) is None:
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
    if kind == "gauge" and d["motion"] == "stretch":
        return scale_along(stretch_direction(d), stretch_factor(d, inputs, path), d["origin"])
    if kind == "gauge":
        a = gauge_amount(d, inputs, path)
        if d["motion"] == "rotate":
            return about(rot(axis, math.degrees(a)), d["pivot"])
        return m4(IDENT, [u * a for u in unit])
    if kind == "roll":
        return about(rot(axis, math.degrees(roll_angle(d, inputs, path))), d["pivot"])
    raise ValueError(kind)


def part_matrix(parts, pid, inputs, path=None, rigid=False):
    """Drivers apply in list order to the authored geometry (pivots in the authored frame);
    then the `ride` part's rigid matrix is applied on top. With `rigid`, the part's own scales
    (`is_scale`) are left out: what a part riding this one takes."""
    inputs = full_inputs(inputs)
    p = next(q for q in parts if q["id"] == pid)
    m = m4()
    for d in p["drivers"]:
        if not (rigid and is_scale(d)):
            m = m4mul(driver_matrix(d, inputs, path), m)
    if p.get("ride"):
        m = m4mul(part_matrix(parts, p["ride"], inputs, path, rigid=True), m)
    return m


# The mill's four-input signatures, kept so its generator's calls stay as they were.
def driver_matrix_mill(d, theta, depth, lifting=0.0, travel=None):
    return driver_matrix(d, {"theta": theta, "depth": depth, "lifting": lifting, "travel": travel})


def part_matrix_mill(parts, pid, theta, depth, lifting=0.0, travel=None):
    return part_matrix(parts, pid, {"theta": theta, "depth": depth, "lifting": lifting, "travel": travel})


# ---------------------------------------------------------------- posing elements, finding parts
def _orthogonal_columns(a, tol=1e-9):
    """Whether the 3x3 `a`'s columns are mutually orthogonal (relative to their lengths)."""
    cols = [[a[i][j] for i in range(3)] for j in range(3)]
    for i in range(3):
        for j in range(i + 1, 3):
            dot = sum(cols[i][k] * cols[j][k] for k in range(3))
            n = math.sqrt(sum(x * x for x in cols[i]) * sum(x * x for x in cols[j]))
            if abs(dot) > tol * max(n, 1e-30):
                return False
    return True


def posed(el: El, m) -> El:
    """`el` (voxels) moved by a part matrix (blocks). A stretch scales the element's extent along
    its axis (the rope is axis-aligned, so this stays a box). A matrix that is a rotation times a
    scale along the authored axes (every rigid part, every `stretch`) takes the first branch; any
    other (a stretching gauge along a turned axis) scales each of the element's own axes by how far
    the matrix stretches it, and raises if the element would come out sheared (the stretch is not
    along one of its own axes). A matrix that collapses the part (a stretch to 0) gives an element
    of size 0 at the point it collapses to (`vanished`)."""
    out = el.clone()
    lin = [row[:3] for row in m[:3]]
    t = [m[i][3] * 16 for i in range(3)]
    if all(v == 0.0 for row in lin for v in row):
        out.c = t
        out.size = [0.0, 0.0, 0.0]
        return out
    axes = [mvec(lin, [el.r[i][k] for i in range(3)]) for k in range(3)]
    lens = [math.sqrt(sum(x * x for x in v)) for v in axes]
    if any(abs(n - 1) > 1e-9 for n in lens):
        for i in range(3):
            for j in range(i + 1, 3):
                if abs(sum(axes[i][k] * axes[j][k] for k in range(3))) > 1e-6 * max(lens[i] * lens[j], 1e-30):
                    raise ValueError(f"{el.name}: its part's stretch shears it (stretch along one of its own axes)")
    if not _orthogonal_columns(lin):
        out.c = [mvec(lin, el.c)[i] + t[i] for i in range(3)]
        out.size = [el.size[k] * lens[k] for k in range(3)]
        # an axis squeezed to nothing keeps its direction from the rigid part of the motion
        cols = [[v / n for v in a] if n > 1e-12 else None for a, n in zip(axes, lens)]
        if any(c is None for c in cols):
            raise ValueError(f"{el.name}: a stretch to 0 must collapse the whole part")
        out.r = [[cols[k][i] for k in range(3)] for i in range(3)]
        return out
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
