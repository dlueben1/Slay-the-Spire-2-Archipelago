import json
import re
import sys
import unittest
from pathlib import Path


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from render_release_notes import render_notes


REPO = Path(__file__).resolve().parents[2]
RELEASE_NOTES_TEMPLATE = REPO / "scripts/release-notes-template.md"
# Published notes keep their links forever, so they point at the docs branch that is kept for
# stable documentation links rather than at a code branch.
DOCS_BRANCH_SETUP_GUIDE = (
    "https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/blob/"
    "setup-docs-do-not-delete/world/spire2/docs/setup_en.md"
)


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


class ReleaseNotesTemplateTests(unittest.TestCase):
    def setUp(self):
        self.template = RELEASE_NOTES_TEMPLATE.read_text(encoding="utf-8")

    def test_ci_renderer_accepts_the_template(self):
        rendered = render_notes(self.template, {
            "CLIENT_VERSION": "2.0.0",
            "WORLD_VERSION": "1.2.0",
            "STS2_PUBLIC_VERSION": "0.107.1",
            "STS2_BETA_VERSION": "0.111.0",
        })
        self.assertIn("- **Archipelago Client:** 2.0.0", rendered)

    def test_setup_guide_links_point_to_the_docs_branch(self):
        links = re.findall(r"[^\s()<>\"']*setup_en\.md", self.template)
        self.assertTrue(links, "The release notes must link to the setup guide.")
        self.assertEqual(set(links), {DOCS_BRANCH_SETUP_GUIDE})

    def test_ritsulib_requirement_matches_the_client_manifest(self):
        manifest = json.loads(
            (REPO / "client/StS2AP/Archipelago.json").read_text(encoding="utf-8-sig")
        )
        minimum = next(
            dependency["min_version"]
            for dependency in manifest["dependencies"]
            if dependency["id"] == "STS2-RitsuLib"
        )
        self.assertIn(f"**STS2-RitsuLib {minimum}+**", self.template)


if __name__ == "__main__":
    unittest.main()
