"""Deterministic writers for shapes, rigs and reference poses, and the origin shift.

Moved verbatim from the bucking sawmill's generator: one element, cell, part or pose per line,
values rounded, a fixed key order, so two runs write the same bytes and a diff shows what changed.
"""

from __future__ import annotations

import copy
import json

from .geometry import TEX_SIZE, El, euler_xyz
from .rigmath import posed

FACE_ORDER = ("north", "east", "south", "west", "up", "down")


def r4(x, n=4):
    v = round(x, n)
    return 0.0 if v == 0 else v


def element_json(el: El):
    """A flattened element as a shape element: from/to about its centre, rotation as Euler
    angles about its centre (only when turned), faces with explicit UVs."""
    half = [abs(s) / 2 for s in el.size]
    e = {"name": el.name,
         "from": [r4(el.c[k] - half[k]) for k in range(3)],
         "to": [r4(el.c[k] + half[k]) for k in range(3)]}
    a = euler_xyz(el.r)
    if any(abs(v) > 1e-4 for v in a):
        e["rotationOrigin"] = [r4(v) for v in el.c]
        for key, v in zip(("rotationX", "rotationY", "rotationZ"), a):
            if abs(v) > 1e-4:
                e[key] = r4(v)
    faces = {}
    for d in FACE_ORDER:
        if d not in el.faces:
            continue
        f = el.faces[d]
        out = {"texture": f["texture"], "uv": [r4(v, 3) for v in f["uv"]]}
        if f.get("rotation"):
            out["rotation"] = f["rotation"]
        out["autoUv"] = False
        faces[d] = out
    e["faces"] = faces
    return e


def shape_json(els, comment, textures, tex_size=TEX_SIZE):
    """A shape file's content: `textures` (code -> path) cut down to the codes the elements use."""
    used = {f["texture"].lstrip("#") for el in els for f in el.faces.values()}
    textures = {k: v for k, v in textures.items() if k in used}
    return {
        "_comment": comment,
        "textureWidth": tex_size, "textureHeight": tex_size,
        "textureSizes": {k: [tex_size, tex_size] for k in textures},
        "textures": textures,
        "elements": [element_json(el) for el in els],
    }


def shape_dumps(shape):
    head = {k: v for k, v in shape.items() if k != "elements"}
    lines = ["{"]
    for k, v in head.items():
        lines.append(f"\t{json.dumps(k)}: {json.dumps(v, separators=(', ', ': '))},")
    lines.append('\t"elements": [')
    lines.append(",\n".join("\t\t" + json.dumps(e, separators=(",", ":")) for e in shape["elements"]))
    lines.append("\t]")
    lines.append("}")
    return "\n".join(lines) + "\n"


def rig_dumps(rig, list_keys=("cells", "parts")):
    """A rig file: the `list_keys` arrays one item per line, every other key on one line."""
    lines = ["{"]
    keys = list(rig)
    for i, k in enumerate(keys):
        end = "," if i < len(keys) - 1 else ""
        v = rig[k]
        if k in list_keys:
            lines.append(f"\t{json.dumps(k)}: [")
            lines.append(",\n".join("\t\t" + json.dumps(x, separators=(", ", ": ")) for x in v))
            lines.append("\t]" + end)
        else:
            lines.append(f"\t{json.dumps(k)}: {json.dumps(v, separators=(', ', ': '))}{end}")
    lines.append("}")
    return "\n".join(lines) + "\n"


def reference_dumps(ref):
    """A reference-poses file, {"_comment", "poses": [...]}: one pose per line."""
    lines = ["{", f'\t"_comment": {json.dumps(ref["_comment"])},', '\t"poses": [']
    lines.append(",\n".join("\t\t" + json.dumps(p, separators=(",", ":")) for p in ref["poses"]))
    lines.append("\t]")
    lines.append("}")
    return "\n".join(lines) + "\n"


def round_matrix(m, places=6):
    """A part matrix's first three rows, rounded, as a reference file stores it."""
    return [[round(v, places) for v in row] for row in m[:3]]


# ---------------------------------------------------------------- the origin shift
def shift_parts(parts, db, keys=("pivot", "anchor")):
    """The parts with every driver's points (`keys`) moved by `db` blocks, rounded to 6 places
    (a copy). Build the model in a frame of your choosing, then move it so the controller is
    [0,0,0]."""
    out = copy.deepcopy(parts)
    for p in out:
        for drv in p.get("drivers", []):
            for key in keys:
                if key in drv:
                    drv[key] = [round(drv[key][k] + db[k], 6) for k in range(3)]
    return out


def shift_cell(pos, origin_cell):
    return [pos[k] - origin_cell[k] for k in range(3)]


def shift_point(p, db, places=4):
    return [round(p[k] + db[k], places) for k in range(3)]


def worst_shift_error(els, ship_els, matrix, ship_matrix, d, poses):
    """How far (voxels) any element posed by the shipped rig lands from the checked one moved by
    `d` voxels, over `poses`. `matrix(el, pose)` and `ship_matrix(el, pose)` give the part
    matrices of the checked and shipped rigs."""
    worst = 0.0
    for pose in poses:
        for a, b in zip(els, ship_els):
            pa, pb = posed(a, matrix(a, pose)), posed(b, ship_matrix(b, pose))
            worst = max(worst, max(abs(pa.c[k] + d[k] - pb.c[k]) for k in range(3)))
    return worst
