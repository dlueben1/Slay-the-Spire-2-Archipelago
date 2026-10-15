#!/usr/bin/env python3
"""Build and publish reviewed Slay the Spire II Archipelago releases.

The source manifests are authoritative. This tool never edits source files,
creates commits, or pushes a branch. ``build`` creates complete release assets;
``publish`` creates a GitHub draft for an explicitly selected, pushed branch.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Sequence
from xml.etree import ElementTree

if __package__:
    from . import client_archive, build_apworld_local
else:
    import client_archive
    import build_apworld_local

APWORLD_ARCHIVE_NAME = client_archive.APWORLD_ARCHIVE_NAME
CLIENT_ARCHIVE_NAME = client_archive.CLIENT_ARCHIVE_NAME
EXPECTED_MOD_ID = client_archive.EXPECTED_MOD_ID
SUPPORTED_STS2_API_COMPATS = client_archive.SUPPORTED_STS2_API_COMPATS
VARIANT_MANIFEST_NAME = client_archive.VARIANT_MANIFEST_NAME
ReleaseError = client_archive.ReleaseError
SemVer = client_archive.SemVer
create_client_archive = client_archive.create_client_archive
include_client_file = client_archive.include_client_file
verify_client_archive = client_archive.verify_client_archive


BUILD_MANIFEST_NAME = "release-build.json"
CLIENT_MANIFEST_PATH = Path("client/StS2AP/Archipelago.json")
WORLD_MANIFEST_PATH = Path("world/spire2/archipelago.json")
CLIENT_PROJECT_PATH = Path("client/StS2AP/StS2AP.csproj")
CLIENT_LOADER_PROJECT_PATH = Path("client/StS2AP.Loader/StS2AP.Loader.csproj")
RELEASE_NOTES_PATH = Path("scripts/release-notes-template.md")
EXPECTED_WORLD_GAME = "Slay the Spire II"


@dataclass(frozen=True)
class Versions:
    mod: SemVer
    apworld: SemVer


@dataclass(frozen=True)
class BuildPaths:
    repo: Path
    archipelago: Path

    @property
    def dist(self) -> Path:
        return self.repo / "dist"

    @property
    def client_archive(self) -> Path:
        return self.dist / CLIENT_ARCHIVE_NAME

    @property
    def apworld_archive(self) -> Path:
        return self.dist / APWORLD_ARCHIVE_NAME

    @property
    def yaml_template(self) -> Path:
        return self.dist / "Spire2-template.yaml"

    @property
    def assets(self) -> tuple[Path, ...]:
        return self.client_archive, self.apworld_archive, self.yaml_template

    @property
    def build_manifest(self) -> Path:
        return self.dist / BUILD_MANIFEST_NAME


def log(message: str = "") -> None:
    print(message, flush=True)


def run(
    command: Sequence[str | os.PathLike[str]],
    *,
    cwd: Path,
    capture: bool = False,
) -> str:
    rendered = " ".join(str(part) for part in command)
    log(f"+ {rendered}")
    try:
        result = subprocess.run(
            [str(part) for part in command],
            cwd=cwd,
            check=False,
            text=True,
            stdout=subprocess.PIPE if capture else None,
            stderr=subprocess.PIPE if capture else None,
        )
    except OSError as exc:
        raise ReleaseError(f"Could not run {command[0]!s}: {exc}") from exc
    if result.returncode != 0:
        details = ""
        if capture:
            details = "\n" + "\n".join(
                part.strip() for part in (result.stdout, result.stderr) if part.strip()
            )
        raise ReleaseError(
            f"Command failed with exit code {result.returncode}: {rendered}{details}"
        )
    return result.stdout.strip() if capture else ""


def load_json(path: Path, label: str) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ReleaseError(f"Could not read {label} at {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise ReleaseError(f"{label} at {path} must contain a JSON object")
    return value


def read_versions(repo: Path) -> Versions:
    client_manifest = load_json(repo / CLIENT_MANIFEST_PATH, "client mod manifest")
    if client_manifest.get("id") != EXPECTED_MOD_ID:
        raise ReleaseError(
            f"{CLIENT_MANIFEST_PATH} id must be {EXPECTED_MOD_ID!r}; got {client_manifest.get('id')!r}"
        )
    mod = SemVer.parse(client_manifest.get("version"), "client mod version")

    world_manifest = load_json(repo / WORLD_MANIFEST_PATH, "APWorld manifest")
    if world_manifest.get("game") != EXPECTED_WORLD_GAME:
        raise ReleaseError(
            f"{WORLD_MANIFEST_PATH} game must be {EXPECTED_WORLD_GAME!r}; got {world_manifest.get('game')!r}"
        )
    apworld = SemVer.parse(world_manifest.get("world_version"), "APWorld version")
    try:
        project_version = ElementTree.parse(repo / CLIENT_PROJECT_PATH).findtext(".//Version")
    except (OSError, ElementTree.ParseError) as exc:
        raise ReleaseError(f"Could not read client project version: {exc}") from exc
    if project_version != str(mod):
        raise ReleaseError(f"Client version mismatch: csproj declares {project_version}, manifest declares {mod}")
    return Versions(mod=mod, apworld=apworld)


def assert_expected_versions(args: argparse.Namespace, versions: Versions) -> None:
    for argument_name, actual, label in (
        ("expected_mod_version", versions.mod, "mod"),
        ("expected_apworld_version", versions.apworld, "APWorld"),
    ):
        expected_text = getattr(args, argument_name, None)
        if expected_text is None:
            continue
        expected = SemVer.parse(expected_text, f"expected {label} version")
        if str(expected) != str(actual):
            raise ReleaseError(
                f"Expected {label} version {expected}, but the checked-in manifest declares {actual}"
            )


def git(repo: Path, *arguments: str, capture: bool = True) -> str:
    return run(("git", *arguments), cwd=repo, capture=capture)


def current_commit(repo: Path) -> str:
    return git(repo, "rev-parse", "HEAD")


def release_input_changes(repo: Path) -> str:
    tracked = git(repo, "status", "--porcelain", "--untracked-files=no")
    untracked_inputs = git(
        repo,
        "ls-files",
        "--others",
        "--exclude-standard",
        "--",
        "client/StS2AP",
        "client/StS2AP.Loader",
        "world/spire2",
    )
    return "\n".join(part for part in (tracked, untracked_inputs) if part)


def assert_reproducible_source(repo: Path, allow_dirty: bool) -> None:
    changes = release_input_changes(repo)
    if changes and not allow_dirty:
        raise ReleaseError(
            "Release inputs are not reproducible. Commit/stash tracked changes and remove or ignore "
            "untracked files under the client projects or world/spire2, or use --allow-dirty for a local test build.\n"
            + changes
        )


@dataclass(frozen=True)
class SemVerSortKey:
    version: SemVer

    def __lt__(self, other: "SemVerSortKey") -> bool:
        return self.version.compare_precedence(other.version) < 0


def strict_semver_tags(repo: Path, *, merged_ref: str = "HEAD") -> list[tuple[SemVer, str]]:
    output = git(repo, "tag", "--merged", merged_ref, "--list")
    versions: list[tuple[SemVer, str]] = []
    for tag in output.splitlines():
        try:
            versions.append((SemVer.parse(tag, f"tag {tag}"), tag))
        except ReleaseError:
            continue
    versions.sort(key=lambda item: SemVerSortKey(item[0]))
    return versions


def version_at_tag(repo: Path, tag: str, path: Path, field: str, label: str) -> SemVer:
    raw = git(repo, "show", f"{tag}:{path.as_posix()}")
    try:
        value = json.loads(raw)
    except json.JSONDecodeError as exc:
        raise ReleaseError(f"Could not parse {path} at tag {tag}: {exc}") from exc
    if not isinstance(value, dict):
        raise ReleaseError(f"{path} at tag {tag} does not contain a JSON object")
    return SemVer.parse(value.get(field), label)


def assert_versions_advance_for_publish(repo: Path, versions: Versions, repository: str | None = None) -> None:
    tags = strict_semver_tags(repo)
    if repository:
        remote_names = set(run(("gh", "api", f"repos/{repository}/tags", "--paginate", "--jq", ".[].name"),
                               cwd=repo, capture=True).splitlines())
        tags = [(version, tag) for version, tag in tags if tag in remote_names]
    if not tags:
        return
    previous_mod, previous_tag = tags[-1]
    if versions.mod.compare_precedence(previous_mod) <= 0:
        raise ReleaseError(
            f"Mod version {versions.mod} must be greater than the latest semantic-version tag "
            f"reachable from the selected source ({previous_tag})"
        )
    previous_apworld = version_at_tag(
        repo,
        previous_tag,
        WORLD_MANIFEST_PATH,
        "world_version",
        f"APWorld version at tag {previous_tag}",
    )
    if versions.apworld.compare_precedence(previous_apworld) < 0:
        raise ReleaseError(
            f"APWorld version {versions.apworld} cannot be lower than {previous_apworld} from tag {previous_tag}"
        )
    world_changes = git(
        repo,
        "diff",
        "--name-only",
        f"{previous_tag}..HEAD",
        "--",
        "world/spire2",
    )
    if world_changes and versions.apworld.compare_precedence(previous_apworld) <= 0:
        raise ReleaseError(
            f"world/spire2 changed after {previous_tag}, so APWorld version {versions.apworld} "
            f"must be greater than {previous_apworld}. Changed paths:\n{world_changes}"
        )


def build_apworld(paths: BuildPaths) -> None:
    build_apworld_local.build(paths.repo, paths.archipelago, paths.apworld_archive)


def build_client(paths: BuildPaths, versions: Versions, signature_root: Path | None) -> None:
    # A fresh staging directory prevents stale files and never deploys to the installed game.
    with tempfile.TemporaryDirectory(prefix="client-release-", dir=paths.dist) as temporary:
        staging = Path(temporary)
        references = ["-p:UseSts2RefLib=true"] if signature_root is None else [
            "-p:UseSts2RefLib=false", f"-p:Sts2ApiSignatureRoot={signature_root}",
        ]
        run((
            "dotnet", "build", paths.repo / CLIENT_PROJECT_PATH, "-c", "Release",
            "-p:BuildMode=Package", f"-p:ModsOutputDir={staging}",
            f"-p:ApWorldSource={paths.apworld_archive}", f"-p:PythonExe={sys.executable}",
            *references,
        ), cwd=paths.repo)
        loader = paths.repo / "client/StS2AP.Loader/bin/Release/net9.0/Archipelago.Loader.dll"
        if not loader.is_file() or not (staging / "Archipelago.dll").is_file():
            raise ReleaseError("Package build did not produce the loader")
        if sha256(staging / "Archipelago.dll") != sha256(loader):
            raise ReleaseError("Root Archipelago.dll must be the compatibility loader")
        entries = {p.relative_to(staging).as_posix(): p
                   for p in staging.rglob("*") if include_client_file(p)}
        create_client_archive(entries, paths.client_archive, str(versions.mod))


def verify_apworld_archive(path: Path, versions: Versions) -> None:
    try:
        with zipfile.ZipFile(path) as archive:
            names = set(archive.namelist())
            corrupt = archive.testzip()
            manifest = json.loads(archive.read("spire2/archipelago.json"))
    except (OSError, KeyError, json.JSONDecodeError, zipfile.BadZipFile) as exc:
        raise ReleaseError(f"APWorld archive is invalid: {path}: {exc}") from exc
    if corrupt is not None:
        raise ReleaseError(f"APWorld archive contains a corrupt entry: {corrupt}")
    required = {"spire2/__init__.py", "spire2/world.py", "spire2/archipelago.json"}
    missing = sorted(required - names)
    if missing:
        raise ReleaseError(f"{path.name} is missing required files: {', '.join(missing)}")
    archive_version = SemVer.parse(manifest.get("world_version"), "built APWorld version")
    if str(archive_version) != str(versions.apworld):
        raise ReleaseError(
            f"Built APWorld declares version {archive_version}, expected {versions.apworld}"
        )


def verify_bundled_apworld(client_archive: Path, standalone_apworld: Path) -> None:
    try:
        with zipfile.ZipFile(client_archive) as archive:
            bundled = archive.read(APWORLD_ARCHIVE_NAME)
    except (OSError, KeyError, zipfile.BadZipFile) as exc:
        raise ReleaseError(
            f"Could not read bundled {APWORLD_ARCHIVE_NAME} from {client_archive}: {exc}"
        ) from exc
    bundled_hash = hashlib.sha256(bundled).hexdigest()
    standalone_hash = sha256(standalone_apworld)
    if bundled_hash != standalone_hash:
        raise ReleaseError(
            f"{client_archive.name} contains a different {APWORLD_ARCHIVE_NAME} "
            f"({bundled_hash}) than the standalone release asset ({standalone_hash})"
        )


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as file:
        for chunk in iter(lambda: file.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def write_build_manifest(
    paths: BuildPaths,
    versions: Versions,
    *,
    source_dirty: bool,
) -> None:
    payload = {
        "schema": 1,
        "commit": current_commit(paths.repo),
        "source_dirty": source_dirty,
        "mod_version": str(versions.mod),
        "apworld_version": str(versions.apworld),
        "assets": {path.name: sha256(path) for path in paths.assets},
    }
    paths.build_manifest.write_text(
        json.dumps(payload, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


def validate_built_assets(paths: BuildPaths, versions: Versions) -> dict[str, Any]:
    verify_client_archive(paths.client_archive, str(versions.mod))
    verify_apworld_archive(paths.apworld_archive, versions)
    verify_bundled_apworld(paths.client_archive, paths.apworld_archive)
    manifest = load_json(paths.build_manifest, "release build manifest")
    expected = {
        "commit": current_commit(paths.repo),
        "mod_version": str(versions.mod),
        "apworld_version": str(versions.apworld),
    }
    for field, value in expected.items():
        if manifest.get(field) != value:
            raise ReleaseError(
                f"{BUILD_MANIFEST_NAME} {field} is {manifest.get(field)!r}, expected {value!r}; rebuild the artifacts"
            )
    if manifest.get("source_dirty") is not False:
        raise ReleaseError(
            f"{BUILD_MANIFEST_NAME} was produced from dirty or unknown source state; rebuild from clean source"
        )
    assets = manifest.get("assets")
    if not isinstance(assets, dict):
        raise ReleaseError(f"{BUILD_MANIFEST_NAME} is missing its assets object")
    for path in paths.assets:
        expected_hash = assets.get(path.name)
        actual_hash = sha256(path)
        if expected_hash != actual_hash:
            raise ReleaseError(
                f"{path.name} SHA-256 is {actual_hash}, expected {expected_hash}; rebuild the artifacts"
            )
    return manifest


def command_validate(args: argparse.Namespace, paths: BuildPaths) -> None:
    versions = read_versions(paths.repo)
    assert_expected_versions(args, versions)
    assert_reproducible_source(paths.repo, args.allow_dirty)
    log(f"Mod version:     {versions.mod}")
    log(f"APWorld version: {versions.apworld}")
    log(f"Source commit:   {current_commit(paths.repo)}")


def command_build(args: argparse.Namespace, paths: BuildPaths) -> None:
    versions = read_versions(paths.repo)
    assert_expected_versions(args, versions)
    assert_reproducible_source(paths.repo, args.allow_dirty)
    source_commit = current_commit(paths.repo)
    source_dirty = bool(release_input_changes(paths.repo))
    paths.dist.mkdir(parents=True, exist_ok=True)
    for path in (*paths.assets, paths.build_manifest):
        path.unlink(missing_ok=True)
    log(f"Building mod {versions.mod} with APWorld {versions.apworld}")
    build_apworld(paths)
    verify_apworld_archive(paths.apworld_archive, versions)
    build_client(paths, versions, args.sts2_api_signature_root.resolve() if args.sts2_api_signature_root else None)
    verify_bundled_apworld(paths.client_archive, paths.apworld_archive)
    shutil.copy2(paths.repo / "world/spire2/docs/Spire2-template.yaml", paths.yaml_template)
    if current_commit(paths.repo) != source_commit:
        raise ReleaseError("Source commit changed during the build; rebuild before publishing")
    write_build_manifest(
        paths,
        versions,
        source_dirty=source_dirty or bool(release_input_changes(paths.repo)),
    )
    log("\nRelease assets ready:")
    for path in paths.assets:
        log(f"  {path} ({path.stat().st_size} bytes, sha256 {sha256(path)})")


def assert_publishable_branch(repo: Path, repository: str, branch: str) -> None:
    current_branch = git(repo, "branch", "--show-current")
    if current_branch != branch:
        raise ReleaseError(f"Publishing requires branch {branch!r}; current branch is {current_branch!r}")
    local_head = current_commit(repo)
    remote_head = run(("gh", "api", f"repos/{repository}/git/ref/heads/{branch}",
                       "--jq", ".object.sha"), cwd=repo, capture=True)
    if local_head != remote_head:
        raise ReleaseError(f"Local {branch} ({local_head}) is not {repository}/{branch} ({remote_head})")


def repository_from_remote(repo: Path, remote: str) -> str:
    url = git(repo, "remote", "get-url", remote)
    match = re.search(r"github\.com[/:]([^/]+/[^/]+?)(?:\.git)?$", url)
    if match is None:
        raise ReleaseError(
            f"Could not infer a GitHub owner/repository from {remote} URL {url!r}; pass --repo OWNER/REPO"
        )
    return match.group(1)


def render_release_notes(repo: Path, versions: Versions, destination: Path) -> None:
    try:
        template = (repo / RELEASE_NOTES_PATH).read_text(encoding="utf-8")
    except OSError as exc:
        raise ReleaseError(f"Could not read release notes template: {exc}") from exc
    content = (
        template.replace("{{VERSION}}", str(versions.mod))
        .replace("{{MOD_VERSION}}", str(versions.mod))
        .replace("{{APWORLD_VERSION}}", str(versions.apworld))
        .replace("{{CLIENT_VERSION}}", str(versions.mod))
        .replace("{{WORLD_VERSION}}", str(versions.apworld))
        .replace("{{STS2_PUBLIC_VERSION}}", SUPPORTED_STS2_API_COMPATS[0])
        .replace("{{STS2_BETA_VERSION}}", SUPPORTED_STS2_API_COMPATS[1])
    )
    content = content.replace(f"spire2-{versions.apworld}.apworld", APWORLD_ARCHIVE_NAME)
    destination.write_text(content, encoding="utf-8")


def command_publish(args: argparse.Namespace, paths: BuildPaths) -> None:
    versions = read_versions(paths.repo)
    assert_expected_versions(args, versions)
    assert_reproducible_source(paths.repo, allow_dirty=False)
    repository = repository_from_remote(paths.repo, args.remote)
    if args.repo and args.repo.casefold() != repository.casefold():
        raise ReleaseError("--repo must match --remote so the reviewed source and release destination agree")
    assert_publishable_branch(paths.repo, repository, args.branch)
    assert_versions_advance_for_publish(paths.repo, versions, repository)
    validate_built_assets(paths, versions)

    tag = str(versions.mod)
    # Check the chosen repository; fork beta tags do not reserve upstream release names.
    remote_tags = json.loads(run(("gh", "api", f"repos/{repository}/git/matching-refs/tags/{tag}"),
                                 cwd=paths.repo, capture=True))
    if any(ref["ref"] == f"refs/tags/{tag}" for ref in remote_tags):
        raise ReleaseError(f"Tag {tag!r} already exists in {repository}; refusing to reuse it")
    with tempfile.TemporaryDirectory(prefix="sts2-release-") as temporary:
        notes = args.notes_file.resolve() if args.notes_file else Path(temporary) / "release-notes.md"
        if args.notes_file:
            if not notes.is_file():
                raise ReleaseError(f"Release notes do not exist: {notes}")
        else:
            render_release_notes(paths.repo, versions, notes)
        command = ["gh", "release", "create", tag, *paths.assets, "--repo", repository,
                   "--target", current_commit(paths.repo), "--draft", "--title",
                   f"Client {versions.mod} / APWorld {versions.apworld}", "--notes-file", notes]
        if args.prerelease:
            command.append("--prerelease")
        run(command, cwd=paths.repo)
    log(f"Created draft in {repository}: client {versions.mod}, APWorld {versions.apworld}. Review before publishing.")


def add_common_arguments(parser: argparse.ArgumentParser, *, allow_dirty: bool) -> None:
    parser.add_argument("--expected-mod-version")
    parser.add_argument("--expected-apworld-version")
    if allow_dirty:
        parser.add_argument(
            "--allow-dirty",
            action="store_true",
            help="allow a non-reproducible local validation/build (never accepted by publish)",
        )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    validate = subparsers.add_parser("validate", help="validate committed versions and source state")
    add_common_arguments(validate, allow_dirty=True)

    build = subparsers.add_parser("build", help="build and verify both release artifacts")
    add_common_arguments(build, allow_dirty=True)
    build.add_argument(
        "--archipelago-root",
        type=Path,
        help="Archipelago checkout (default: sibling ../Archipelago)",
    )
    build.add_argument(
        "--sts2-api-signature-root",
        type=Path,
        help="optional local compile-time assemblies; defaults to NuGet reference assemblies",
    )

    publish = subparsers.add_parser(
        "publish",
        help="create a draft release from a reviewed, pushed commit and verified artifacts",
    )
    add_common_arguments(publish, allow_dirty=False)
    publish.add_argument("--remote", default="origin")
    publish.add_argument("--branch", required=True, help="reviewed branch, for example main or v2")
    publish.add_argument("--notes-file", type=Path, help="reviewed Markdown release notes")
    publish.add_argument("--prerelease", action="store_true", help="mark the draft as a prerelease")
    publish.add_argument("--repo", help="GitHub OWNER/REPO (inferred from --remote by default)")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    repo = Path(__file__).resolve().parents[1]
    archipelago_root = getattr(args, "archipelago_root", None)
    archipelago = archipelago_root.resolve() if archipelago_root else repo.parent / "Archipelago"
    paths = BuildPaths(repo=repo, archipelago=archipelago)
    try:
        if args.command == "validate":
            command_validate(args, paths)
        elif args.command == "build":
            command_build(args, paths)
        elif args.command == "publish":
            command_publish(args, paths)
        else:
            parser.error(f"Unknown command: {args.command}")
    except ReleaseError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
