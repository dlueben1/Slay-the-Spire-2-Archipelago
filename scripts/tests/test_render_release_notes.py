import sys
import unittest
from pathlib import Path


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from render_release_notes import render_notes


class RenderReleaseNotesTests(unittest.TestCase):
    def setUp(self):
        self.values = {
            "CLIENT_VERSION": "1.2.0",
            "WORLD_VERSION": "1.1.1",
            "STS2_PUBLIC_VERSION": "0.107.1",
            "STS2_BETA_VERSION": "0.111.0",
        }
        self.template = " ".join("{{" + key + "}}" for key in self.values)

    def test_renders_all_required_values(self):
        rendered = render_notes(self.template, self.values)
        self.assertEqual(rendered, "1.2.0 1.1.1 0.107.1 0.111.0")

    def test_missing_required_placeholder_fails(self):
        with self.assertRaisesRegex(ValueError, "is missing"):
            render_notes(self.template.replace("{{CLIENT_VERSION}}", ""), self.values)

    def test_unknown_placeholder_fails(self):
        with self.assertRaisesRegex(ValueError, "Unresolved placeholders"):
            render_notes(self.template + " {{UNKNOWN_VALUE}}", self.values)


if __name__ == "__main__":
    unittest.main()
