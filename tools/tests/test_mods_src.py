"""Mods built in mods-src/ stay in step with the copy pinned from the ModDB.

Each mods-src/<modid>/ mod is built here, uploaded to the ModDB by hand and pinned in
pack/pack.toml like any other mod (mods-src/allowedvariantsfix/README.md). Two copies can
drift, so: the .csproj and modinfo.json agree on the version, and once the mod is pinned,
the pin is the version in the source. A version bump in mods-src/ therefore fails here until
that version is uploaded and pinned, which is the reminder to do it.

Run with `python3 -m unittest discover -s tools/tests`.
"""

import importlib.util
import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
_spec = importlib.util.spec_from_file_location("packtool", ROOT / "tools" / "packtool.py")
packtool = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(packtool)

MODS = sorted((ROOT / "mods-src").glob("*/modinfo.json"))


class ModsSrcInStep(unittest.TestCase):
    def test_there_is_something_to_check(self):
        self.assertTrue(MODS, "no mods-src/*/modinfo.json found")

    def test_directory_is_named_after_the_modid(self):
        for path in MODS:
            self.assertEqual(json.loads(path.read_text())["modid"], path.parent.name, path)

    def test_csproj_version_matches_modinfo(self):
        for path in MODS:
            (csproj,) = path.parent.glob("*.csproj")
            found = re.search(r"<Version>([^<]+)</Version>", csproj.read_text())
            assert found is not None, f"{csproj}: no <Version>"
            self.assertEqual(found.group(1), json.loads(path.read_text())["version"], csproj)

    def test_pin_is_the_source_version(self):
        pinned = {m["id"]: m["version"] for m in packtool.load_pack()["mod"]}
        for path in MODS:
            info = json.loads(path.read_text())
            if info["modid"] not in pinned:
                continue  # not uploaded yet
            self.assertEqual(
                pinned[info["modid"]], info["version"],
                f"pack.toml pins {info['modid']}@{pinned[info['modid']]} but {path} is "
                f"{info['version']}: upload that build to the ModDB and pin it",
            )


if __name__ == "__main__":
    unittest.main()
