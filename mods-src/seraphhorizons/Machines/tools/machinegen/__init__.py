"""Shared, machine-free parts of the machines' model generators (stdlib only).

    geometry  El and the box builders: flatten, from_template, beam, strut, octagon, rotate, ...
    rigmath   the rig's driver maths (the reference for C# and TypeScript), posed, part_of
    checks    validation helpers: z-fighting (coplanar_faces, fix_coplanar), OBB tests, cell boxes,
              containment, clearances, frame connectivity, shaft supports
    output    the deterministic JSON writers: shapes, rigs, reference poses

A generator imports it with

    sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Machines" / "tools"))

from `mods-src/seraphhorizons/<Machine>/tools/make_shape.py`.
"""
