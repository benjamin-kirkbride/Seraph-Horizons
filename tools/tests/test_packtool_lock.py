"""The committed lock carries what its readers need from it."""

import json
import unittest
from pathlib import Path

LOCK = Path(__file__).resolve().parents[2] / "pack" / "lock.json"


class LockAssetIds(unittest.TestCase):
    def test_every_mod_has_its_moddb_asset_id(self):
        # The recipe site links a mod to /show/mod/<assetId>: its /<alias> is often not the
        # modid. A lock written before `packtool lock` recorded it needs a re-lock.
        mods = json.loads(LOCK.read_text())["mods"]
        missing = [m["id"] for m in mods if not (isinstance(m.get("assetId"), int) and m["assetId"] > 0)]
        self.assertEqual(missing, [], "re-run `tools/packtool.py lock`")
        ids = [m["assetId"] for m in mods]
        self.assertEqual(len(ids), len(set(ids)), "two mods share an asset id")


if __name__ == "__main__":
    unittest.main()
