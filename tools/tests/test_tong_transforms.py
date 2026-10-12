"""Every collectible of seraphhorizons that is held in tongs has both of the game's tong poses.

A hot item in the hand is drawn in the tongs held in the other hand, posed by an attribute the tongs
name (their `transformCode`): the game's wooden `game:tongs` read `onTongTransform`, its metal
`game:tongsmetal-*` read `onMetalTongTransform`. A missing one falls back to `onTongTransform`, then to
a default, both posed for a different grip, so the item hangs off the tongs (the steel spring did, in
steel tongs). So a collectible that sets either pose sets both, in its own files or in a patch, whose
ops are grouped by the file they patch. Needs no game and no mod zips.
"""

from __future__ import annotations

import unittest
from pathlib import Path

from test_gear_consumers import PATCHES, loads

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "mods-src" / "seraphhorizons" / "assets" / "seraphhorizons"
POSES = {"onTongTransform", "onMetalTongTransform"}


def _docs(path: Path) -> list:
    doc = loads(path.read_text(encoding="utf-8-sig"))
    return doc if isinstance(doc, list) else [doc]


def _poses(node) -> set[str]:
    """The tong pose keys anywhere in a document or patch value."""
    found = set()
    if isinstance(node, dict):
        for key, value in node.items():
            if key in POSES:
                found.add(key)
            found |= _poses(value)
    elif isinstance(node, list):
        for value in node:
            found |= _poses(value)
    return found


class TongTransformTests(unittest.TestCase):
    def test_types_set_both_poses_or_neither(self):
        paths = sorted([*(ASSETS / "itemtypes").rglob("*.json"), *(ASSETS / "blocktypes").rglob("*.json")])
        self.assertTrue(paths)
        posed = 0
        for path in paths:
            for doc in _docs(path):
                poses = _poses(doc)
                with self.subTest(asset=str(path.relative_to(ASSETS)), code=doc.get("code")):
                    self.assertIn(poses, (set(), POSES), "sets one tong pose and not the other")
                posed += bool(poses)
        # the steel spring, pipe section, angle, half plate, gear blanks, molds, pot and pan at least
        self.assertGreaterEqual(posed, 11)

    def test_patches_set_both_poses_or_neither(self):
        by_target: dict[str, set[str]] = {}
        for path in sorted(PATCHES.rglob("*.json")):
            for op in _docs(path):
                if not isinstance(op, dict):
                    continue
                keys = {key for key in POSES if key in str(op.get("path", ""))} | _poses(op.get("value"))
                if keys:
                    by_target.setdefault(str(op.get("file")), set()).update(keys)
        for target, poses in by_target.items():
            with self.subTest(target=target):
                self.assertEqual(POSES, poses, "a patch sets one tong pose and not the other")


if __name__ == "__main__":
    unittest.main()
