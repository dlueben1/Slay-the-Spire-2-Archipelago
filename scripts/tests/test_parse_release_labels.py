import sys
import unittest
from pathlib import Path


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from parse_release_labels import LabelError, parse_labels


class ParseReleaseLabelsTests(unittest.TestCase):
    def test_client_patch_only(self):
        self.assertEqual(
            parse_labels(["release-client-patch"]),
            {
                "should_release": "true",
                "client_bump": "patch",
                "world_bump": "none",
            },
        )

    def test_client_minor_and_world_patch(self):
        self.assertEqual(
            parse_labels(["release-world-patch", "release-client-minor"]),
            {
                "should_release": "true",
                "client_bump": "minor",
                "world_bump": "patch",
            },
        )

    def test_no_release_labels_skips(self):
        self.assertEqual(
            parse_labels(["documentation", "release-web"])["should_release"],
            "false",
        )

    def test_conflicting_client_labels_fail(self):
        with self.assertRaisesRegex(LabelError, "Conflicting client"):
            parse_labels(["release-client-major", "release-client-patch"])

    def test_conflicting_world_labels_fail(self):
        with self.assertRaisesRegex(LabelError, "Conflicting APWorld"):
            parse_labels(
                [
                    "release-client-patch",
                    "release-world-minor",
                    "release-world-patch",
                ]
            )

    def test_world_without_client_fails(self):
        with self.assertRaisesRegex(LabelError, "requires exactly one"):
            parse_labels(["release-world-patch"])


if __name__ == "__main__":
    unittest.main()
