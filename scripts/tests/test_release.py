from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path


MODULE_PATH = Path(__file__).parents[1] / "release.py"
SPEC = importlib.util.spec_from_file_location("sts2_release", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
release = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = release
SPEC.loader.exec_module(release)


class SemVerTests(unittest.TestCase):
    def parse(self, value: str):
        return release.SemVer.parse(value, "test version")

    def test_semver_precedence(self) -> None:
        ordered = [
            "1.0.0-alpha",
            "1.0.0-alpha.1",
            "1.0.0-alpha.beta",
            "1.0.0-beta",
            "1.0.0-beta.2",
            "1.0.0-beta.11",
            "1.0.0-rc.1",
            "1.0.0",
            "1.0.1",
        ]
        parsed = [self.parse(value) for value in ordered]
        for lower, higher in zip(parsed, parsed[1:]):
            self.assertLess(lower.compare_precedence(higher), 0)

    def test_build_metadata_does_not_change_precedence(self) -> None:
        self.assertEqual(
            self.parse("1.2.3+first").compare_precedence(self.parse("1.2.3+second")),
            0,
        )

    def test_rejects_loose_or_zero_padded_versions(self) -> None:
        for value in ("v1.2.3", "1.2", "01.2.3", "1.2.3-alpha.01"):
            with self.subTest(value=value), self.assertRaises(release.ReleaseError):
                self.parse(value)


class VersionSourceTests(unittest.TestCase):
    def test_reads_independent_versions_from_tracked_sources(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            repo = Path(temporary)
            client = repo / release.CLIENT_MANIFEST_PATH
            world_manifest = repo / release.WORLD_MANIFEST_PATH
            world_source = repo / release.WORLD_SOURCE_PATH
            for path in (client, world_manifest, world_source):
                path.parent.mkdir(parents=True, exist_ok=True)
            client.write_text(
                json.dumps({"id": "Archipelago", "version": "1.4.2"}),
                encoding="utf-8",
            )
            world_manifest.write_text(
                json.dumps({"game": "Slay the Spire II", "world_version": "1.1.0"}),
                encoding="utf-8",
            )
            world_source.write_text(
                "class SlayTheSpire2World:\n    mod_compat_version = '1.1.0'\n",
                encoding="utf-8",
            )

            versions = release.read_versions(repo)

            self.assertEqual(str(versions.mod), "1.4.2")
            self.assertEqual(str(versions.apworld), "1.1.0")

    def test_rejects_apworld_version_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            repo = Path(temporary)
            client = repo / release.CLIENT_MANIFEST_PATH
            world_manifest = repo / release.WORLD_MANIFEST_PATH
            world_source = repo / release.WORLD_SOURCE_PATH
            for path in (client, world_manifest, world_source):
                path.parent.mkdir(parents=True, exist_ok=True)
            client.write_text(
                json.dumps({"id": "Archipelago", "version": "1.4.2"}),
                encoding="utf-8",
            )
            world_manifest.write_text(
                json.dumps({"game": "Slay the Spire II", "world_version": "1.1.0"}),
                encoding="utf-8",
            )
            world_source.write_text(
                "class SlayTheSpire2World:\n    mod_compat_version = '1.0.0'\n",
                encoding="utf-8",
            )

            with self.assertRaisesRegex(release.ReleaseError, "APWorld version mismatch"):
                release.read_versions(repo)


class ClientArchiveTests(unittest.TestCase):
    def make_valid_entries(self, root: Path) -> dict[str, Path | bytes]:
        inputs = root / "inputs"
        inputs.mkdir()
        entries: dict[str, Path | bytes] = {}
        for name in (
            "Archipelago.json",
            "Archipelago.dll",
            "Archipelago.pck",
            "Archipelago.MultiClient.Net.dll",
            "spire2.apworld",
        ):
            path = inputs / name
            if name == "Archipelago.json":
                path.write_text(
                    json.dumps({"id": "Archipelago", "version": "1.0.0"}),
                    encoding="utf-8",
                )
            else:
                path.write_bytes(name.encode())
            entries[name] = path

        variants = {}
        for compat in release.SUPPORTED_STS2_API_COMPATS:
            dll = inputs / f"Archipelago-{compat}.dll"
            dll.write_bytes(f"variant-{compat}".encode())
            assembly = f"lib/{compat}/Archipelago.dll"
            entries[assembly] = dll
            entries[f"lib/{compat}/compat-target.txt"] = f"{compat}\n".encode()
            variants[compat] = {"assembly": assembly, "sha256": release.sha256(dll)}
        entries[release.VARIANT_MANIFEST_NAME] = json.dumps(
            {"schema": 1, "modVersion": "1.0.0", "variants": variants}
        ).encode()
        return entries

    def test_creates_versioned_variant_archive(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            entries = self.make_valid_entries(root)
            archive_path = root / "Archipelago.zip"

            release.create_client_archive(entries, archive_path)

            with zipfile.ZipFile(archive_path) as archive:
                self.assertEqual(archive.namelist(), sorted(entries))

    def test_rejects_unexpected_nested_archive_path(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive_path = root / "Archipelago.zip"
            release.create_client_archive(self.make_valid_entries(root), archive_path)
            with zipfile.ZipFile(archive_path, "a") as archive:
                archive.writestr("Archipelago/unexpected.dll", b"test")

            with self.assertRaisesRegex(release.ReleaseError, "Unexpected nested"):
                release.verify_client_archive(archive_path)

    def test_rejects_forbidden_build_artifacts(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive_path = root / "Archipelago.zip"
            release.create_client_archive(self.make_valid_entries(root), archive_path)
            with zipfile.ZipFile(archive_path, "a") as archive:
                archive.writestr("sts2.dll", b"test")

            with self.assertRaisesRegex(release.ReleaseError, "forbidden files"):
                release.verify_client_archive(archive_path)

    def test_rejects_different_bundled_and_standalone_apworlds(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            client_archive = root / "Archipelago.zip"
            standalone = root / "spire2.apworld"
            standalone.write_bytes(b"standalone")
            with zipfile.ZipFile(client_archive, "w") as archive:
                archive.writestr("spire2.apworld", b"different")

            with self.assertRaisesRegex(release.ReleaseError, "different spire2.apworld"):
                release.verify_bundled_apworld(client_archive, standalone)


class PublishVersionTests(unittest.TestCase):
    def git(self, repo: Path, *arguments: str) -> None:
        subprocess.run(
            ("git", *arguments),
            cwd=repo,
            check=True,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )

    def make_tagged_repo(self, repo: Path) -> None:
        self.git(repo, "init", "-b", "main")
        self.git(repo, "config", "user.name", "Release Test")
        self.git(repo, "config", "user.email", "release-test@example.invalid")
        client = repo / release.CLIENT_MANIFEST_PATH
        world_manifest = repo / release.WORLD_MANIFEST_PATH
        world_source = repo / release.WORLD_SOURCE_PATH
        for path in (client, world_manifest, world_source):
            path.parent.mkdir(parents=True, exist_ok=True)
        client.write_text(
            json.dumps({"id": "Archipelago", "version": "1.0.0"}),
            encoding="utf-8",
        )
        world_manifest.write_text(
            json.dumps({"game": "Slay the Spire II", "world_version": "1.0.0"}),
            encoding="utf-8",
        )
        world_source.write_text(
            "class SlayTheSpire2World:\n    mod_compat_version = '1.0.0'\n",
            encoding="utf-8",
        )
        self.git(repo, "add", ".")
        self.git(repo, "commit", "-m", "release 1.0.0")
        self.git(repo, "tag", "1.0.0")

    def test_allows_client_only_release_with_unchanged_apworld_version(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            repo = Path(temporary)
            self.make_tagged_repo(repo)
            client = repo / release.CLIENT_MANIFEST_PATH
            client.write_text(
                json.dumps({"id": "Archipelago", "version": "1.0.1"}),
                encoding="utf-8",
            )
            self.git(repo, "add", ".")
            self.git(repo, "commit", "-m", "client release")

            release.assert_versions_advance_for_publish(repo, release.read_versions(repo))

    def test_requires_apworld_bump_when_world_sources_change(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            repo = Path(temporary)
            self.make_tagged_repo(repo)
            client = repo / release.CLIENT_MANIFEST_PATH
            client.write_text(
                json.dumps({"id": "Archipelago", "version": "1.0.1"}),
                encoding="utf-8",
            )
            (repo / "world/spire2/options.py").write_text("changed = True\n", encoding="utf-8")
            self.git(repo, "add", ".")
            self.git(repo, "commit", "-m", "world changed without version")

            with self.assertRaisesRegex(release.ReleaseError, "must be greater"):
                release.assert_versions_advance_for_publish(repo, release.read_versions(repo))


if __name__ == "__main__":
    unittest.main()
