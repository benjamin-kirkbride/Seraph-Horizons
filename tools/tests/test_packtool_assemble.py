"""packtool assemble never ships the repository's local tool mods.

tools/icon-export (seraphiconfix), tools/recipe-export (seraphexport) and tools/ore-survey
(seraphoresurvey) are mods that live in this repository for CI, for exporting icons and for
measuring worldgen; they must never reach players. assemble builds
every release artifact from pack/lock.json alone, so the check is that the lock and pack.toml
do not name them and that nothing assemble writes mentions them.

Run with `python3 -m unittest discover -s tools/tests`.
"""

import argparse
import contextlib
import importlib.util
import io
import json
import tempfile
import unittest
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
_spec = importlib.util.spec_from_file_location("packtool", ROOT / "tools" / "packtool.py")
packtool = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(packtool)

# Written out rather than discovered, so a renamed tool mod fails here instead of silently
# dropping out of the check.
TOOL_MODS = {
    "seraphiconfix": ROOT / "tools" / "icon-export" / "modinfo.json",
    "seraphexport": ROOT / "tools" / "recipe-export" / "modinfo.json",
    "seraphoresurvey": ROOT / "tools" / "ore-survey" / "modinfo.json",
}


class ToolModsStayOut(unittest.TestCase):
    def test_tool_mod_ids_are_what_this_test_expects(self):
        for modid, path in TOOL_MODS.items():
            self.assertEqual(json.loads(path.read_text())["modid"], modid, path)

    def test_lock_and_pack_do_not_name_them(self):
        lock_ids = {m["id"] for m in packtool.load_lock()["mods"]}
        pack_ids = {m["id"] for m in packtool.load_pack().get("mod", [])}
        for modid in TOOL_MODS:
            self.assertNotIn(modid, lock_ids)
            self.assertNotIn(modid, pack_ids)

    def test_nothing_assemble_writes_mentions_them(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp) / "dist"
            lock = packtool.load_lock()
            real_load_lock = packtool.load_lock
            # Redistributable zips would come from the fetch cache; the test has none, and
            # which third-party zips ship is not what it checks.
            packtool.load_lock = lambda: {**lock, "mods": [{**m, "redistribute": False} for m in lock["mods"]]}
            try:
                with contextlib.redirect_stdout(io.StringIO()):
                    packtool.cmd_assemble(argparse.Namespace(out=str(out), cache=str(Path(tmp) / "cache")))
            finally:
                packtool.load_lock = real_load_lock
            blobs = []
            for p in sorted(out.iterdir()):
                if p.suffix == ".zip":
                    with zipfile.ZipFile(p) as z:
                        blobs += [(f"{p.name}:{n}", z.read(n)) for n in z.namelist()]
                        blobs += [(f"{p.name} member name", n.encode()) for n in z.namelist()]
                else:
                    blobs.append((p.name, p.read_bytes()))
            self.assertGreater(len(blobs), 3)
            for name, data in blobs:
                for modid in TOOL_MODS:
                    self.assertNotIn(modid.encode(), data.lower(), name)
                self.assertNotIn(b"icon-export", data, name)
                self.assertNotIn(b"ore-survey", data, name)


if __name__ == "__main__":
    unittest.main()
