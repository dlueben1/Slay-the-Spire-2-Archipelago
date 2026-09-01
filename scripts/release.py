#!/usr/bin/env python3
"""Build and publish reviewed Slay the Spire II Archipelago releases.

The source manifests are authoritative. This tool never edits source files,
creates commits, or pushes a branch. ``build`` creates complete release assets;
``publish`` tags the already-reviewed main commit and uploads those assets.
"""

from __future__ import annotations

import argparse
import ast
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
from typing import Any, Iterable, Sequence


CLIENT_ARCHIVE_NAME = "Archipelago.zip"
APWORLD_ARCHIVE_NAME = "spire2.apworld"
BUILD_MANIFEST_NAME = "release-build.json"
CLIENT_MANIFEST_PATH = Path("client/StS2AP/Archipelago.json")
WORLD_MANIFEST_PATH = Path("world/spire2/archipelago.json")
WORLD_SOURCE_PATH = Path("world/spire2/world.py")
CLIENT_PROJECT_PATH = Path("client/StS2AP/StS2AP.csproj")
RELEASE_NOTES_PATH = Path("scripts/release-notes-template.md")
EXPECTED_MOD_ID = "Archipelago"
EXPECTED_WORLD_GAME = "Slay the Spire II"
EXCLUDED_CLIENT_FILES = {"0Harmony.dll", "GodotSharp.dll", "sts2.dll"}
REQUIRED_CLIENT_FILES = {
    "Archipelago.json",
    "Archipelago.dll",
    "Archipelago.pck",
    APWORLD_ARCHIVE_NAME,
}


class ReleaseError(RuntimeError):
    """A release precondition or external command failed."""


@dataclass(frozen=True)
class SemVer:
    major: int
    minor: int
    patch: int
    prerelease: tuple[str, ...] = ()
    build: str | None = None

    _PATTERN = re.compile(
        r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)"
        r"(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?"
        r"(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$"
    )

    @classmethod
    def parse(cls, value: object, label: str) -> "SemVer":
        if not isinstance(value, str):
            raise ReleaseError(f"{label} must be a semantic-version string")
        match = cls._PATTERN.fullmatch(value)
        if match is None:
            raise ReleaseError(
                f"{label} must be a strict semantic version (X.Y.Z with optional prerelease/build metadata); got {value!r}"
            )
        prerelease_text = match.group(4)
        prerelease = tuple(prerelease_text.split(".")) if prerelease_text else ()
        for identifier in prerelease:
            if identifier.isdigit() and len(identifier) > 1 and identifier.startswith("0"):
                raise ReleaseError(
                    f"{label} has a numeric prerelease identifier with a leading zero: {value!r}"
                )
        return cls(
            int(match.group(1)),
            int(match.group(2)),
            int(match.group(3)),
            prerelease,
            match.group(5),
        )

    def __str__(self) -> str:
        value = f"{self.major}.{self.minor}.{self.patch}"
        if self.prerelease:
            value += "-" + ".".join(self.prerelease)
        if self.build:
            value += "+" + self.build
        return value

    def precedence_key(self) -> tuple[int, int, int]:
        return self.major, self.minor, self.patch

    def compare_precedence(self, other: "SemVer") -> int:
        if self.precedence_key() != other.precedence_key():
            return 1 if self.precedence_key() > other.precedence_key() else -1
        if not self.prerelease and not other.prerelease:
            return 0
        if not self.prerelease:
            return 1
        if not other.prerelease:
            return -1
        for left, right in zip(self.prerelease, other.prerelease):
            if left == right:
                continue
            left_numeric = left.isdigit()
            right_numeric = right.isdigit()
            if left_numeric and right_numeric:
                return 1 if int(left) > int(right) else -1
            if left_numeric != right_numeric:
                return -1 if left_numeric else 1
            return 1 if left > right else -1
        if len(self.prerelease) == len(other.prerelease):
            return 0
        return 1 if len(self.prerelease) > len(other.prerelease) else -1


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


def read_world_source_version(path: Path) -> str:
    try:
        tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
    except (OSError, SyntaxError) as exc:
        raise ReleaseError(f"Could not parse {path}: {exc}") from exc
    for node in ast.walk(tree):
        if not isinstance(node, ast.ClassDef) or node.name != "SlayTheSpire2World":
            continue
        for statement in node.body:
            if not isinstance(statement, (ast.Assign, ast.AnnAssign)):
                continue
            targets = statement.targets if isinstance(statement, ast.Assign) else [statement.target]
            if not any(isinstance(target, ast.Name) and target.id == "mod_compat_version" for target in targets):
                continue
            value = statement.value
            if isinstance(value, ast.Constant) and isinstance(value.value, str):
                return value.value
            raise ReleaseError(
                f"SlayTheSpire2World.mod_compat_version in {path} must be a string literal"
            )
    raise ReleaseError(f"Could not find SlayTheSpire2World.mod_compat_version in {path}")


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
    source_version = SemVer.parse(
        read_world_source_version(repo / WORLD_SOURCE_PATH),
        "APWorld slot-data version",
    )
    if str(source_version) != str(apworld):
        raise ReleaseError(
            f"APWorld version mismatch: {WORLD_MANIFEST_PATH} declares {apworld}, "
            f"but {WORLD_SOURCE_PATH} declares {source_version}"
        )
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
        "world/spire2",
    )
    return "\n".join(part for part in (tracked, untracked_inputs) if part)


def assert_reproducible_source(repo: Path, allow_dirty: bool) -> None:
    changes = release_input_changes(repo)
    if changes and not allow_dirty:
        raise ReleaseError(
            "Release inputs are not reproducible. Commit/stash tracked changes and remove or ignore "
            "untracked files under client/StS2AP or world/spire2, or use --allow-dirty for a local test build.\n"
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


def assert_versions_advance_for_publish(repo: Path, versions: Versions) -> None:
    tags = strict_semver_tags(repo)
    if not tags:
        return
    previous_mod, previous_tag = tags[-1]
    if versions.mod.compare_precedence(previous_mod) <= 0:
        raise ReleaseError(
            f"Mod version {versions.mod} must be greater than the latest semantic-version tag "
            f"reachable from main ({previous_tag})"
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
    launcher = paths.archipelago / "Launcher.py"
    worlds_dir = paths.archipelago / "worlds"
    destination = worlds_dir / "spire2"
    source = paths.repo / "world/spire2"
    if not launcher.is_file() or not worlds_dir.is_dir():
        raise ReleaseError(
            f"Archipelago checkout not found at {paths.archipelago}. "
            "Pass --archipelago-root with a checkout containing Launcher.py and worlds/."
        )

    log(f"Syncing {source} to {destination} (existing contents will be deleted)")
    if destination.exists():
        shutil.rmtree(destination)
    shutil.copytree(source, destination)

    run(
        (sys.executable, launcher, "Build APWorlds", EXPECTED_WORLD_GAME),
        cwd=paths.archipelago,
    )
    built = paths.archipelago / "build/apworlds" / APWORLD_ARCHIVE_NAME
    if not built.is_file():
        raise ReleaseError(f"Archipelago build succeeded but did not create {built}")
    shutil.copy2(built, paths.apworld_archive)


def parse_msbuild_properties(output: str) -> dict[str, Any]:
    try:
        value = json.loads(output)
    except json.JSONDecodeError as exc:
        raise ReleaseError(f"dotnet msbuild returned invalid property JSON: {exc}") from exc
    properties = value.get("Properties") if isinstance(value, dict) else None
    if not isinstance(properties, dict):
        raise ReleaseError("dotnet msbuild output did not contain a Properties object")
    return properties


def build_client(paths: BuildPaths, versions: Versions) -> None:
    project = paths.repo / CLIENT_PROJECT_PATH
    build_properties = (
        f"-p:Version={versions.mod}",
        f"-p:ModName={EXPECTED_MOD_ID}",
    )
    run(
        ("dotnet", "build", project, "-c", "Release", *build_properties),
        cwd=paths.repo,
    )
    property_output = run(
        (
            "dotnet",
            "msbuild",
            project,
            "-getProperty:ModsOutputDir",
            "-getProperty:ModName",
            *build_properties,
        ),
        cwd=paths.repo,
        capture=True,
    )
    properties = parse_msbuild_properties(property_output)
    mods_output = Path(str(properties.get("ModsOutputDir", "")))
    mod_name = str(properties.get("ModName", ""))
    if not mods_output.is_dir() or not mod_name:
        raise ReleaseError(
            f"Could not resolve a valid ModsOutputDir and ModName from MSBuild: {properties}"
        )
    pck = mods_output / f"{mod_name}.pck"
    if not pck.is_file():
        raise ReleaseError(
            f"Godot export did not produce {pck}. Check GodotExePath in client/StS2AP/local.props."
        )

    output = paths.repo / "client/StS2AP/bin/Release/net9.0"
    if not output.is_dir():
        raise ReleaseError(f"Client build output directory does not exist: {output}")
    files = [path for path in output.iterdir() if include_client_file(path)]
    files.append(pck)
    files.append(paths.apworld_archive)
    create_flat_client_archive(files, paths.client_archive, versions)


def include_client_file(path: Path) -> bool:
    if not path.is_file():
        return False
    if path.name in EXCLUDED_CLIENT_FILES:
        return False
    if path.suffix.lower() in {".pdb", ".xml"}:
        return False
    if path.name.endswith(".deps.json"):
        return False
    if path.name.startswith("STS2.RitsuLib") and path.suffix.lower() == ".dll":
        return False
    return True


def create_flat_client_archive(
    files: Iterable[Path],
    destination: Path,
    versions: Versions | None = None,
) -> None:
    selected: dict[str, Path] = {}
    for path in files:
        if not path.is_file():
            raise ReleaseError(f"Client release input does not exist: {path}")
        if path.name in selected and selected[path.name] != path:
            raise ReleaseError(
                f"Two client release inputs have the same filename {path.name}: "
                f"{selected[path.name]} and {path}"
            )
        selected[path.name] = path
    missing = sorted(REQUIRED_CLIENT_FILES - selected.keys())
    if missing:
        raise ReleaseError(f"Client release is missing required files: {', '.join(missing)}")
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.unlink(missing_ok=True)
    with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name in sorted(selected):
            archive.write(selected[name], arcname=name)
    verify_client_archive(destination, versions)


def verify_client_archive(path: Path, versions: Versions | None = None) -> None:
    try:
        with zipfile.ZipFile(path) as archive:
            names = archive.namelist()
            corrupt = archive.testzip()
            manifest_bytes = (
                archive.read("Archipelago.json")
                if "Archipelago.json" in names
                else None
            )
    except (OSError, zipfile.BadZipFile) as exc:
        raise ReleaseError(f"Client archive is invalid: {path}: {exc}") from exc
    if corrupt is not None:
        raise ReleaseError(f"Client archive contains a corrupt entry: {corrupt}")
    if any("/" in name.strip("/") or "\\" in name.strip("\\") for name in names):
        raise ReleaseError(
            f"{path.name} must be flat so archive tools create the Archipelago install directory"
        )
    missing = sorted(REQUIRED_CLIENT_FILES - set(names))
    if missing:
        raise ReleaseError(f"{path.name} is missing required files: {', '.join(missing)}")
    try:
        manifest = json.loads(manifest_bytes)
    except (TypeError, json.JSONDecodeError) as exc:
        raise ReleaseError(f"{path.name} contains an invalid Archipelago.json: {exc}") from exc
    if not isinstance(manifest, dict) or manifest.get("id") != EXPECTED_MOD_ID:
        raise ReleaseError(
            f"{path.name} contains a client manifest without id {EXPECTED_MOD_ID!r}"
        )
    archive_version = SemVer.parse(manifest.get("version"), "built client version")
    if versions is not None and str(archive_version) != str(versions.mod):
        raise ReleaseError(
            f"Built client declares version {archive_version}, expected {versions.mod}"
        )
    forbidden = sorted(name for name in names if not include_client_archive_name(name))
    if forbidden:
        raise ReleaseError(f"{path.name} contains forbidden files: {', '.join(forbidden)}")


def include_client_archive_name(name: str) -> bool:
    path = Path(name)
    if path.name in EXCLUDED_CLIENT_FILES:
        return False
    if path.suffix.lower() in {".pdb", ".xml"}:
        return False
    if path.name.endswith(".deps.json"):
        return False
    if path.name.startswith("STS2.RitsuLib") and path.suffix.lower() == ".dll":
        return False
    return True


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
        "assets": {
            CLIENT_ARCHIVE_NAME: sha256(paths.client_archive),
            APWORLD_ARCHIVE_NAME: sha256(paths.apworld_archive),
        },
    }
    paths.build_manifest.write_text(
        json.dumps(payload, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


def validate_built_assets(paths: BuildPaths, versions: Versions) -> dict[str, Any]:
    verify_client_archive(paths.client_archive, versions)
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
            f"{BUILD_MANIFEST_NAME} was produced from dirty or unknown source state; rebuild from clean main"
        )
    assets = manifest.get("assets")
    if not isinstance(assets, dict):
        raise ReleaseError(f"{BUILD_MANIFEST_NAME} is missing its assets object")
    for path in (paths.client_archive, paths.apworld_archive):
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
    paths.dist.mkdir(parents=True, exist_ok=True)
    for path in (paths.client_archive, paths.apworld_archive, paths.build_manifest):
        path.unlink(missing_ok=True)
    log(f"Building mod {versions.mod} with APWorld {versions.apworld}")
    build_apworld(paths)
    verify_apworld_archive(paths.apworld_archive, versions)
    build_client(paths, versions)
    verify_bundled_apworld(paths.client_archive, paths.apworld_archive)
    write_build_manifest(
        paths,
        versions,
        source_dirty=bool(release_input_changes(paths.repo)),
    )
    log("\nRelease assets ready:")
    for path in (paths.client_archive, paths.apworld_archive):
        log(f"  {path} ({path.stat().st_size} bytes, sha256 {sha256(path)})")


def assert_publishable_main(repo: Path, remote: str) -> None:
    branch = git(repo, "branch", "--show-current")
    if branch != "main":
        raise ReleaseError(f"Publishing requires branch 'main'; current branch is {branch!r}")
    local_head = current_commit(repo)
    remote_line = git(repo, "ls-remote", "--heads", remote, "refs/heads/main")
    remote_head = remote_line.split(maxsplit=1)[0] if remote_line else ""
    if local_head != remote_head:
        raise ReleaseError(
            f"Local main ({local_head}) is not the reviewed {remote}/main commit ({remote_head or 'missing'})"
        )


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
    )
    destination.write_text(content, encoding="utf-8")


def command_publish(args: argparse.Namespace, paths: BuildPaths) -> None:
    versions = read_versions(paths.repo)
    assert_expected_versions(args, versions)
    assert_reproducible_source(paths.repo, allow_dirty=False)
    assert_publishable_main(paths.repo, args.remote)
    assert_versions_advance_for_publish(paths.repo, versions)
    validate_built_assets(paths, versions)

    tag = str(versions.mod)
    try:
        git(paths.repo, "rev-parse", "--verify", f"refs/tags/{tag}")
    except ReleaseError:
        pass
    else:
        raise ReleaseError(f"Tag {tag!r} already exists; refusing to move or reuse a release tag")

    repository = args.repo or repository_from_remote(paths.repo, args.remote)
    with tempfile.TemporaryDirectory(prefix="sts2-release-") as temporary:
        notes = Path(temporary) / "release-notes.md"
        render_release_notes(paths.repo, versions, notes)
        git(paths.repo, "tag", tag, "HEAD", capture=False)
        try:
            git(paths.repo, "push", args.remote, f"refs/tags/{tag}", capture=False)
            run(
                (
                    "gh",
                    "release",
                    "create",
                    tag,
                    paths.client_archive,
                    paths.apworld_archive,
                    "--repo",
                    repository,
                    "--title",
                    f"Client {versions.mod} / APWorld {versions.apworld}",
                    "--notes-file",
                    notes,
                    "--latest",
                ),
                cwd=paths.repo,
            )
        except ReleaseError:
            log(
                f"Tag {tag} may already have been pushed. The branch was not modified; inspect the remote before retrying."
            )
            raise
    log(f"Published {repository} tag {tag}: client {versions.mod}, APWorld {versions.apworld}")


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

    publish = subparsers.add_parser(
        "publish",
        help="tag the reviewed main commit and upload previously built artifacts",
    )
    add_common_arguments(publish, allow_dirty=False)
    publish.add_argument("--remote", default="origin")
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
