from __future__ import annotations

from pathlib import Path
import subprocess
import sys
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "print_logic_table.py"


class PrintLogicTableTests(unittest.TestCase):
    def test_prints_every_region_without_archipelago(self):
        # A plain subprocess has no Archipelago on its path, so this also checks that
        # logic_table.py stays free of Archipelago imports.
        output = subprocess.run([sys.executable, str(SCRIPT)], check=True,
                                capture_output=True, text=True).stdout
        sys.path.insert(0, str(SCRIPT.parent))
        try:
            import print_logic_table
        finally:
            sys.path.remove(str(SCRIPT.parent))
        for region in print_logic_table.load_table().REGIONS:
            with self.subTest(region=region):
                self.assertIn(region, output)


if __name__ == "__main__":
    unittest.main()
