"""Mods built in mods-src/ stay in step with the pack.

The .csproj and modinfo.json of every mods-src/<modid>/ mod agree on the version. Then:

- seraphhorizons, the pack's own mod, is released with the pack, from the same v<version> tag
  (release.yml), so it has the pack's version: modinfo.json's equals pack.toml's
  `[pack] version`. One number for both; bump them together.
- The others (allowedvariantsfix, ...) are built here, uploaded to the ModDB by hand and pinned
  in pack/pack.toml like any other mod (mods-src/allowedvariantsfix/README.md). Two copies can
  drift, so once one is pinned, the pin is never ahead of the source. The pin may lag: a version
  bump merges first, is released from a tag on main (mod-release.yml), uploaded, and only then
  pinned.

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

# The pack's own mod: its modid is the pack id.
PACK_MOD = packtool.load_pack()["pack"]["id"]
MODS = sorted((ROOT / "mods-src").glob("*/modinfo.json"))
# Every mod written here, shipped or not, plus the release meta-mod's description (pack.toml).
ALL_MODS = MODS + sorted((ROOT / "tools").glob("*/modinfo.json"))
DESCRIPTION_LIMIT = 100


class Descriptions(unittest.TestCase):
    def test_no_description_is_longer_than_the_limit(self):
        descriptions = {path: json.loads(path.read_text())["description"] for path in ALL_MODS}
        descriptions[ROOT / "pack" / "pack.toml"] =packtool.load_pack()["pack"].get("description", "")
        for source, text in descriptions.items():
            self.assertLessEqual(
                len(text), DESCRIPTION_LIMIT,
                f"{source}: description is {len(text)} characters, over {DESCRIPTION_LIMIT}",
            )


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

    def test_the_packs_own_mod_has_the_packs_version(self):
        pack = packtool.load_pack()["pack"]
        info = json.loads((ROOT / "mods-src" / PACK_MOD / "modinfo.json").read_text())
        self.assertEqual(
            info["version"], pack["version"],
            f"mods-src/{PACK_MOD} is released with the pack (release.yml): its modinfo.json "
            f"version ({info['version']}) must equal pack.toml's [pack] version ({pack['version']})",
        )

    def test_the_packs_own_mod_is_not_pinned(self):
        pinned = {m["id"].lower() for m in packtool.load_pack()["mod"]}
        self.assertNotIn(PACK_MOD.lower(), pinned,
                         f"{PACK_MOD} is released with the pack (release.yml), never pinned in pack.toml")

    def test_pin_is_not_ahead_of_the_source(self):
        pinned = {m["id"]: m["version"] for m in packtool.load_pack()["mod"]}
        for path in MODS:
            info = json.loads(path.read_text())
            if info["modid"] == PACK_MOD:
                continue  # released with the pack, not pinned (test above)
            if info["modid"] not in pinned:
                continue  # not uploaded yet
            pin, source = pinned[info["modid"]], info["version"]
            self.assertLessEqual(
                _release(pin), _release(source),
                f"pack.toml pins {info['modid']}@{pin}, newer than {path} ({source}): "
                "the ModDB copy was not built from this source",
            )


def _release(version: str) -> tuple[int, ...]:
    """1.2.10 as (1, 2, 10), so versions compare numerically. Pre-release tags are not used."""
    return tuple(int(part) for part in version.split("."))

if __name__ == "__main__":
    unittest.main()
