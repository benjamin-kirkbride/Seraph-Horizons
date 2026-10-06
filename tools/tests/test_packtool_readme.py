"""packtool's README status line, which cog writes into README.md from pack.toml.

Run with `python3 -m unittest discover -s tools/tests`.
"""

import importlib.util
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
_spec = importlib.util.spec_from_file_location("packtool", ROOT / "tools" / "packtool.py")
packtool = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(packtool)


class ReadmeStatus(unittest.TestCase):
    def status(self, dotnet: str, mods: int) -> str:
        pack = {"pack": {"game_version": "1.22.7", "dotnet": dotnet}, "mod": [{"id": f"m{i}"} for i in range(mods)]}
        return packtool.readme_status(pack)

    def test_counts_every_mod_table(self):
        self.assertIn("**Mods:** 3,", self.status("10.0", 3))

    def test_dotnet_drops_only_a_zero_minor(self):
        self.assertIn("1.22.7 (.NET 10)", self.status("10.0", 1))
        self.assertIn("(.NET 10.1)", self.status("10.1", 1))


if __name__ == "__main__":
    unittest.main()
