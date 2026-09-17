import json
import sys
import tempfile
import unittest
from pathlib import Path


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from prepare_release import VersionError, bump_version, prepare_release


class PrepareReleaseTests(unittest.TestCase):
    def make_repo(self, client="1.1.1", manifest_client=None, world="1.1.0"):
        temporary = tempfile.TemporaryDirectory()
        root = Path(temporary.name)
        client_dir = root / "client/StS2AP"
        world_dir = root / "world/spire2"
        client_dir.mkdir(parents=True)
        world_dir.mkdir(parents=True)

        (client_dir / "StS2AP.csproj").write_text(
            f"<Project>\n  <PropertyGroup>\n    <Version>{client}</Version>\n"
            "  </PropertyGroup>\n</Project>\n",
            encoding="utf-8",
        )
        (client_dir / "Archipelago.json").write_text(
            json.dumps({"version": manifest_client or client}, indent=2) + "\n",
            encoding="utf-8",
        )
        (world_dir / "archipelago.json").write_text(
            json.dumps({"world_version": world}, indent=2) + "\n",
            encoding="utf-8",
        )
        (world_dir / "world.py").write_text(
            f'class SlayTheSpire2World:\n    mod_compat_version = "{world}"\n',
            encoding="utf-8",
        )
        return temporary, root

    def test_semver_bumps(self):
        self.assertEqual(bump_version("1.2.3", "major"), "2.0.0")
        self.assertEqual(bump_version("1.2.3", "minor"), "1.3.0")
        self.assertEqual(bump_version("1.2.3", "patch"), "1.2.4")

    def test_client_patch_leaves_world_unchanged(self):
        temporary, root = self.make_repo()
        self.addCleanup(temporary.cleanup)

        outputs = prepare_release(root, "patch", "none")

        self.assertEqual(outputs["new_client_version"], "1.1.2")
        self.assertEqual(outputs["new_world_version"], "1.1.0")
        self.assertEqual(outputs["world_changed"], "false")
        self.assertIn(
            "<Version>1.1.2</Version>",
            (root / "client/StS2AP/StS2AP.csproj").read_text(encoding="utf-8"),
        )
        self.assertEqual(
            json.loads((root / "client/StS2AP/Archipelago.json").read_text())["version"],
            "1.1.2",
        )
        self.assertEqual(
            json.loads((root / "world/spire2/archipelago.json").read_text())[
                "world_version"
            ],
            "1.1.0",
        )

    def test_client_minor_and_world_patch(self):
        temporary, root = self.make_repo()
        self.addCleanup(temporary.cleanup)

        outputs = prepare_release(root, "minor", "patch")

        self.assertEqual(outputs["new_client_version"], "1.2.0")
        self.assertEqual(outputs["new_world_version"], "1.1.1")
        self.assertEqual(outputs["world_changed"], "true")
        self.assertIn(outputs["new_world_version"], (root / "world/spire2/world.py").read_text())

    def test_client_major_and_world_minor(self):
        temporary, root = self.make_repo(client="2.7.9", world="4.8.6")
        self.addCleanup(temporary.cleanup)

        outputs = prepare_release(root, "major", "minor")

        self.assertEqual(outputs["new_client_version"], "3.0.0")
        self.assertEqual(outputs["new_world_version"], "4.9.0")

    def test_mismatched_client_versions_fail_without_writes(self):
        temporary, root = self.make_repo(client="1.1.1", manifest_client="1.1.0")
        self.addCleanup(temporary.cleanup)
        project_before = (root / "client/StS2AP/StS2AP.csproj").read_bytes()

        with self.assertRaisesRegex(VersionError, "disagree"):
            prepare_release(root, "patch", "none")

        self.assertEqual(
            (root / "client/StS2AP/StS2AP.csproj").read_bytes(), project_before
        )

    def test_mismatched_world_versions_fail_without_writes(self):
        temporary, root = self.make_repo()
        self.addCleanup(temporary.cleanup)
        (root / "world/spire2/world.py").write_text(
            'class SlayTheSpire2World:\n    mod_compat_version = "0.0.0"\n'
        )
        project = root / "client/StS2AP/StS2AP.csproj"
        before = project.read_bytes()
        with self.assertRaisesRegex(VersionError, "disagree"):
            prepare_release(root, "patch", "patch")
        self.assertEqual(project.read_bytes(), before)

    def test_malformed_version_fails(self):
        temporary, root = self.make_repo(client="1.1")
        self.addCleanup(temporary.cleanup)

        with self.assertRaisesRegex(VersionError, "three-part SemVer"):
            prepare_release(root, "patch", "none")


if __name__ == "__main__":
    unittest.main()
