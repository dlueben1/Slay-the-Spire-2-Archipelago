#!/usr/bin/env python3
"""Validate and bump the client/APWorld release versions."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import TextIO


SEMVER = re.compile(r"(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)")
BUMPS = ("major", "minor", "patch")
CLIENT_PROJECT = Path("client/StS2AP/StS2AP.csproj")
CLIENT_MANIFEST = Path("client/StS2AP/Archipelago.json")
WORLD_MANIFEST = Path("world/spire2/archipelago.json")


class VersionError(ValueError):
    """Raised when release version sources are malformed or inconsistent."""


def validate_version(value: object, source: str) -> str:
    if not isinstance(value, str) or SEMVER.fullmatch(value) is None:
        raise VersionError(f"{source} must contain a three-part SemVer; found {value!r}.")
    return value


def bump_version(version: str, bump: str) -> str:
    if bump not in BUMPS:
        raise VersionError(f"Unsupported version bump {bump!r}.")

    major, minor, patch = (int(part) for part in version.split("."))
    if bump == "major":
        return f"{major + 1}.0.0"
    if bump == "minor":
        return f"{major}.{minor + 1}.0"
    return f"{major}.{minor}.{patch + 1}"


def read_text(path: Path) -> str:
    return path.read_bytes().decode("utf-8")


def replace_exactly_once(
    content: str, pattern: re.Pattern[str], new_value: str, source: str
) -> str:
    matches = list(pattern.finditer(content))
    if len(matches) != 1:
        raise VersionError(
            f"Expected exactly one version field in {source}; found {len(matches)}."
        )
    return pattern.sub(lambda match: f"{match.group(1)}{new_value}{match.group(3)}", content)


def prepare_release(repo_root: Path, client_bump: str, world_bump: str) -> dict[str, str]:
    project_path = repo_root / CLIENT_PROJECT
    client_manifest_path = repo_root / CLIENT_MANIFEST
    world_manifest_path = repo_root / WORLD_MANIFEST

    project_text = read_text(project_path)
    client_manifest_text = read_text(client_manifest_path)
    world_manifest_text = read_text(world_manifest_path)

    project_pattern = re.compile(r"(<Version>)([^<]+)(</Version>)")
    client_pattern = re.compile(r'("version"\s*:\s*")([^"]+)(")')
    world_pattern = re.compile(r'("world_version"\s*:\s*")([^"]+)(")')

    project_matches = list(project_pattern.finditer(project_text))
    if len(project_matches) != 1:
        raise VersionError(
            f"Expected exactly one <Version> in {CLIENT_PROJECT}; found {len(project_matches)}."
        )

    try:
        client_manifest = json.loads(client_manifest_text)
        world_manifest = json.loads(world_manifest_text)
    except json.JSONDecodeError as error:
        raise VersionError(f"Invalid JSON in a release manifest: {error}") from error

    project_version = validate_version(
        project_matches[0].group(2), str(CLIENT_PROJECT)
    )
    manifest_version = validate_version(
        client_manifest.get("version"), str(CLIENT_MANIFEST)
    )
    world_version = validate_version(
        world_manifest.get("world_version"), str(WORLD_MANIFEST)
    )

    if project_version != manifest_version:
        raise VersionError(
            "Client version sources disagree: "
            f"{CLIENT_PROJECT} has {project_version}, while "
            f"{CLIENT_MANIFEST} has {manifest_version}."
        )

    new_client = bump_version(project_version, client_bump)
    if world_bump == "none":
        new_world = world_version
        world_changed = False
    else:
        new_world = bump_version(world_version, world_bump)
        world_changed = True

    new_project_text = replace_exactly_once(
        project_text, project_pattern, new_client, str(CLIENT_PROJECT)
    )
    new_client_manifest_text = replace_exactly_once(
        client_manifest_text, client_pattern, new_client, str(CLIENT_MANIFEST)
    )
    new_world_manifest_text = replace_exactly_once(
        world_manifest_text, world_pattern, new_world, str(WORLD_MANIFEST)
    )

    # Validate everything before writing so malformed input cannot cause a partial bump.
    json.loads(new_client_manifest_text)
    json.loads(new_world_manifest_text)

    project_path.write_bytes(new_project_text.encode("utf-8"))
    client_manifest_path.write_bytes(new_client_manifest_text.encode("utf-8"))
    if world_changed:
        world_manifest_path.write_bytes(new_world_manifest_text.encode("utf-8"))

    return {
        "old_client_version": project_version,
        "new_client_version": new_client,
        "old_world_version": world_version,
        "new_world_version": new_world,
        "world_changed": str(world_changed).lower(),
    }


def write_outputs(outputs: dict[str, str], destination: TextIO) -> None:
    for key, value in outputs.items():
        destination.write(f"{key}={value}\n")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--client-bump", choices=BUMPS, required=True)
    parser.add_argument("--world-bump", choices=(*BUMPS, "none"), default="none")
    parser.add_argument("--repo-root", type=Path, default=Path.cwd())
    parser.add_argument("--github-output", type=Path)
    args = parser.parse_args()

    try:
        outputs = prepare_release(
            args.repo_root.resolve(), args.client_bump, args.world_bump
        )
    except (OSError, VersionError) as error:
        parser.error(str(error))

    if args.github_output:
        with args.github_output.open("a", encoding="utf-8", newline="\n") as destination:
            write_outputs(outputs, destination)
    else:
        write_outputs(outputs, sys.stdout)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
