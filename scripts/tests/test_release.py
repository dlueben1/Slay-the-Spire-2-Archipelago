from __future__ import annotations

import json
import argparse
import shutil
import subprocess
import tempfile
import unittest
import zipfile
from contextlib import ExitStack
from pathlib import Path
from unittest.mock import patch

from scripts import release


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
            world_source = repo / release.CLIENT_PROJECT_PATH
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
                "<Project><PropertyGroup><Version>1.4.2</Version></PropertyGroup></Project>",
                encoding="utf-8",
            )

            versions = release.read_versions(repo)

            self.assertEqual(str(versions.mod), "1.4.2")
            self.assertEqual(str(versions.apworld), "1.1.0")

    def test_rejects_client_project_version_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            repo = Path(temporary)
            client = repo / release.CLIENT_MANIFEST_PATH
            world_manifest = repo / release.WORLD_MANIFEST_PATH
            world_source = repo / release.CLIENT_PROJECT_PATH
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
                "<Project><PropertyGroup><Version>1.0.0</Version></PropertyGroup></Project>",
                encoding="utf-8",
            )

            with self.assertRaisesRegex(release.ReleaseError, "Client version mismatch"):
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
            "Newtonsoft.Json.dll",
            "StS2AP.Domain.dll",
            "FSharp.Core.dll",
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

        for catalog in ("relic_custom_pools.data", "bonus_relic_blacklist.data"):
            entries[f"data/{catalog}"] = b"{}"
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

    def test_accepts_directory_entries_from_make_archive(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            staging = root / "staging"
            for name, source in self.make_valid_entries(root).items():
                destination = staging / name
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.write_bytes(source.read_bytes() if isinstance(source, Path) else source)

            archive_path = Path(shutil.make_archive(str(root / "Archipelago"), "zip", root_dir=staging))
            with zipfile.ZipFile(archive_path) as archive:
                self.assertIn(f"lib/{release.SUPPORTED_STS2_API_COMPATS[0]}/", archive.namelist())
            release.verify_client_archive(archive_path, "1.0.0")

    def test_rejects_client_version_different_from_source(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with self.assertRaisesRegex(release.ReleaseError, "expected 1.0.1"):
                release.create_client_archive(
                    self.make_valid_entries(root), root / "Archipelago.zip", "1.0.1"
                )

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
        for forbidden_name in (
            "sts2.dll",
            "STS2.RitsuLib.dll",
            "STS2-RitsuLib.dll",
            "STS2-RitsuLib.Runtime.dll",
            "STS2-RitsuLib.Shared.dll",
            "STS2-RitsuLib.Ui.dll",
            "STS2-RitsuLib.Settings.dll",
        ):
            with (
                self.subTest(forbidden_name=forbidden_name),
                tempfile.TemporaryDirectory() as temporary,
            ):
                root = Path(temporary)
                archive_path = root / "Archipelago.zip"
                release.create_client_archive(self.make_valid_entries(root), archive_path)
                with zipfile.ZipFile(archive_path, "a") as archive:
                    archive.writestr(forbidden_name, b"test")

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
        world_source = repo / release.CLIENT_PROJECT_PATH
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
            "<Project><PropertyGroup><Version>1.0.0</Version></PropertyGroup></Project>",
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
            (repo / release.CLIENT_PROJECT_PATH).write_text("<Project><PropertyGroup><Version>1.0.1</Version></PropertyGroup></Project>")
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
            (repo / release.CLIENT_PROJECT_PATH).write_text("<Project><PropertyGroup><Version>1.0.1</Version></PropertyGroup></Project>")
            self.git(repo, "add", ".")
            self.git(repo, "commit", "-m", "world changed without version")

            with self.assertRaisesRegex(release.ReleaseError, "must be greater"):
                release.assert_versions_advance_for_publish(repo, release.read_versions(repo))


class ManualReleaseTests(unittest.TestCase):
    def test_publish_defaults_to_draft_for_explicit_branch_and_destination(self):
        args = release.build_parser().parse_args(['publish', '--branch', 'v2', '--remote', 'upstream', '--prerelease'])
        versions = release.Versions(release.SemVer.parse('2.0.0', 'client'), release.SemVer.parse('1.2.0', 'world'))
        with tempfile.TemporaryDirectory() as temporary:
            paths = release.BuildPaths(Path(temporary), Path(temporary)/'ap')
            with patch.object(release, 'read_versions', return_value=versions), \
                 patch.object(release, 'assert_reproducible_source'), \
                 patch.object(release, 'repository_from_remote', return_value='owner/project'), \
                 patch.object(release, 'assert_publishable_branch') as branch, \
                 patch.object(release, 'assert_versions_advance_for_publish'), \
                 patch.object(release, 'validate_built_assets'), \
                 patch.object(release, 'render_release_notes'), \
                 patch.object(release, 'current_commit', return_value='reviewed-sha'), \
                 patch.object(release, 'run', side_effect=['[]', '']) as run:
                release.command_publish(args, paths)
            branch.assert_called_once_with(paths.repo, 'owner/project', 'v2')
            command = run.call_args.args[0]
            self.assertEqual(command[:4], ['gh', 'release', 'create', '2.0.0'])
            self.assertIn('--draft', command)
            self.assertIn('--prerelease', command)
            self.assertNotIn('--latest', command)
            self.assertEqual(command[command.index('--target')+1], 'reviewed-sha')
            self.assertIn(paths.yaml_template, command)

    def test_branch_must_match_pushed_remote_commit(self):
        with patch.object(release, 'git', return_value='v2'), \
             patch.object(release, 'current_commit', return_value='local'), \
             patch.object(release, 'run', return_value='different'):
            with self.assertRaisesRegex(release.ReleaseError, 'is not'):
                release.assert_publishable_branch(Path('.'), 'owner/project', 'v2')

    def test_beta_fork_tags_do_not_block_upstream_public_numbering(self):
        versions = release.Versions(release.SemVer.parse('2.0.0', 'client'), release.SemVer.parse('1.2.0', 'world'))
        with patch.object(release, 'strict_semver_tags', return_value=[(release.SemVer.parse('2.5.5', 'tag'), '2.5.5')]), \
             patch.object(release, 'run', return_value='1.1.0\n'):
            release.assert_versions_advance_for_publish(Path('.'), versions, 'upstream/project')

    def test_manual_notes_reference_the_actual_uploaded_apworld_name(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            template = root/release.RELEASE_NOTES_PATH
            template.parent.mkdir()
            template.write_text('Install spire2-{{WORLD_VERSION}}.apworld')
            versions = release.Versions(release.SemVer.parse('2.0.0', 'client'), release.SemVer.parse('1.2.0', 'world'))
            output = root/'notes.md'
            release.render_release_notes(root, versions, output)
            self.assertEqual(output.read_text(), 'Install spire2.apworld')

    def test_package_build_uses_isolated_staging_and_loader(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            paths = release.BuildPaths(root, root/'ap')
            paths.dist.mkdir()
            entries = ClientArchiveTests().make_valid_entries(root)
            loader = root/'client/StS2AP.Loader/bin/Release/net9.0/Archipelago.Loader.dll'
            loader.parent.mkdir(parents=True)
            loader.write_bytes(b'loader')
            def build(command, **kwargs):
                self.assertIn('-p:BuildMode=Package', command)
                self.assertFalse(any('DllOnlyBuild' in str(arg) for arg in command))
                stage = Path(next(arg.split('=',1)[1] for arg in command if str(arg).startswith('-p:ModsOutputDir=')))
                self.assertTrue(stage.is_relative_to(paths.dist))
                for name, source in entries.items():
                    target=stage/name
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(source.read_bytes() if isinstance(source, Path) else source)
                (stage/'Archipelago.dll').write_bytes(b'loader')
            versions = release.Versions(release.SemVer.parse('1.0.0','client'),release.SemVer.parse('1.0.0','world'))
            with patch.object(release, 'run', side_effect=build):
                release.build_client(paths, versions, None)
            with zipfile.ZipFile(paths.client_archive) as archive:
                self.assertEqual(archive.read('Archipelago.dll'), b'loader')

    def test_publish_rejects_mismatched_destination_before_network_actions(self):
        args = release.build_parser().parse_args(['publish', '--branch', 'main', '--repo', 'wrong/repo'])
        with patch.object(release, 'read_versions'), patch.object(release, 'assert_reproducible_source'), \
             patch.object(release, 'repository_from_remote', return_value='right/repo'), \
             patch.object(release, 'run') as run:
            with self.assertRaisesRegex(release.ReleaseError, '--repo must match'):
                release.command_publish(args, release.BuildPaths(Path('.'), Path('../ap')))
            run.assert_not_called()

    def test_publish_rejects_existing_remote_tag_without_creating_release(self):
        args = release.build_parser().parse_args(['publish', '--branch', 'main'])
        versions = release.Versions(release.SemVer.parse('2.0.0', 'client'), release.SemVer.parse('1.2.0', 'world'))
        with ExitStack() as stack:
            for name in ('assert_reproducible_source', 'assert_publishable_branch',
                         'assert_versions_advance_for_publish', 'validate_built_assets'):
                stack.enter_context(patch.object(release, name))
            stack.enter_context(patch.object(release, 'read_versions', return_value=versions))
            stack.enter_context(patch.object(release, 'repository_from_remote', return_value='right/repo'))
            run = stack.enter_context(patch.object(release, 'run', return_value='[{"ref":"refs/tags/2.0.0"}]'))
            with self.assertRaisesRegex(release.ReleaseError, 'already exists'):
                release.command_publish(args, release.BuildPaths(Path('.'), Path('../ap')))
            self.assertEqual(run.call_count, 1)
            self.assertEqual(run.call_args.args[0][:2], ('gh', 'api'))


if __name__ == "__main__":
    unittest.main()
