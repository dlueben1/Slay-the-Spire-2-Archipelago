"""Select, create, and validate the distributable client ZIP."""

from __future__ import annotations

import hashlib
import json
import re
import zipfile
from dataclasses import dataclass
from pathlib import Path
from typing import Mapping

CLIENT_ARCHIVE_NAME = "Archipelago.zip"
APWORLD_ARCHIVE_NAME = "spire2.apworld"
VARIANT_MANIFEST_NAME = "archipelago-variants.manifest"
SUPPORTED_STS2_API_COMPATS = ("0.107.1", "0.111.0")
EXPECTED_MOD_ID = "Archipelago"
EXCLUDED_CLIENT_FILES = {"0Harmony.dll", "GodotSharp.dll", "sts2.dll"}
RITSULIB_ASSEMBLY_PREFIXES = ("sts2.ritsulib", "sts2-ritsulib")
REQUIRED_CLIENT_FILES = {
    "Archipelago.json",
    "Archipelago.dll",
    "Archipelago.pck",
    "data/relic_custom_pools.data",
    "data/bonus_relic_blacklist.data",
    APWORLD_ARCHIVE_NAME,
    VARIANT_MANIFEST_NAME,
    *(f"lib/{compat}/Archipelago.dll" for compat in SUPPORTED_STS2_API_COMPATS),
    *(f"lib/{compat}/compat-target.txt" for compat in SUPPORTED_STS2_API_COMPATS),
}
ALLOWED_CLIENT_DIRECTORIES = {
    "data/",
    "lib/",
    *(f"lib/{compat}/" for compat in SUPPORTED_STS2_API_COMPATS),
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


def include_client_file(path: Path) -> bool:
    if not path.is_file():
        return False
    if path.name in EXCLUDED_CLIENT_FILES:
        return False
    if path.suffix.lower() in {".pdb", ".xml"}:
        return False
    if path.name.endswith(".deps.json"):
        return False
    if is_ritsulib_assembly(path):
        return False
    return True

def is_ritsulib_assembly(path: Path) -> bool:
    return path.suffix.lower() == ".dll" and path.name.casefold().startswith(
        RITSULIB_ASSEMBLY_PREFIXES
    )


def create_client_archive(
    entries: Mapping[str, Path | bytes],
    destination: Path,
    expected_version: str | None = None,
) -> None:
    selected = dict(entries)
    missing = sorted(REQUIRED_CLIENT_FILES - selected.keys())
    if missing:
        raise ReleaseError(f"Client release is missing required files: {', '.join(missing)}")
    for name, source in selected.items():
        validate_archive_entry_name(name)
        if isinstance(source, Path) and not source.is_file():
            raise ReleaseError(f"Client release input does not exist: {source}")
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.unlink(missing_ok=True)
    with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name in sorted(selected):
            source = selected[name]
            if isinstance(source, Path):
                archive.write(source, arcname=name)
            else:
                archive.writestr(name, source)
    verify_client_archive(destination, expected_version)


def validate_archive_entry_name(name: str) -> None:
    path = Path(name)
    if not name or name.startswith(("/", "\\")) or "\\" in name or ".." in path.parts:
        raise ReleaseError(f"Invalid client archive path: {name!r}")
    if name.endswith("/"):
        if name in ALLOWED_CLIENT_DIRECTORIES:
            return
        raise ReleaseError(f"Unexpected client archive directory: {name!r}")
    if len(path.parts) == 1:
        return
    if len(path.parts) in (2, 3) and name in REQUIRED_CLIENT_FILES:
        return
    raise ReleaseError(f"Unexpected nested client archive path: {name!r}")


def verify_client_archive(path: Path, expected_version: str | None = None) -> None:
    try:
        with zipfile.ZipFile(path) as archive:
            names = archive.namelist()
            corrupt = archive.testzip()
            manifest_bytes = (
                archive.read("Archipelago.json")
                if "Archipelago.json" in names
                else None
            )
            variant_manifest_bytes = (
                archive.read(VARIANT_MANIFEST_NAME)
                if VARIANT_MANIFEST_NAME in names
                else None
            )
    except (OSError, zipfile.BadZipFile) as exc:
        raise ReleaseError(f"Client archive is invalid: {path}: {exc}") from exc
    if corrupt is not None:
        raise ReleaseError(f"Client archive contains a corrupt entry: {corrupt}")
    if len(names) != len(set(names)):
        raise ReleaseError(f"{path.name} contains duplicate archive entries")
    for name in names:
        validate_archive_entry_name(name)
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
    if expected_version is not None and str(archive_version) != expected_version:
        raise ReleaseError(
            f"Built client declares version {archive_version}, expected {expected_version}"
        )
    try:
        variant_manifest = json.loads(variant_manifest_bytes)
    except (TypeError, json.JSONDecodeError) as exc:
        raise ReleaseError(f"{path.name} contains an invalid {VARIANT_MANIFEST_NAME}: {exc}") from exc
    if not isinstance(variant_manifest, dict) or variant_manifest.get("schema") != 1:
        raise ReleaseError(f"{path.name} contains an unsupported variant manifest")
    if variant_manifest.get("modVersion") != str(archive_version):
        raise ReleaseError(f"{path.name} variant manifest modVersion does not match Archipelago.json")
    variants = variant_manifest.get("variants")
    if not isinstance(variants, dict) or tuple(sorted(variants)) != tuple(sorted(SUPPORTED_STS2_API_COMPATS)):
        raise ReleaseError(f"{path.name} must contain exactly the supported STS2 variants")
    with zipfile.ZipFile(path) as archive:
        for compat in SUPPORTED_STS2_API_COMPATS:
            entry = variants.get(compat)
            expected_assembly = f"lib/{compat}/Archipelago.dll"
            if not isinstance(entry, dict) or entry.get("assembly") != expected_assembly:
                raise ReleaseError(f"{path.name} has an invalid assembly path for STS2 {compat}")
            actual_hash = hashlib.sha256(archive.read(expected_assembly)).hexdigest()
            if entry.get("sha256") != actual_hash:
                raise ReleaseError(f"{path.name} has an invalid DLL hash for STS2 {compat}")
            try:
                marker = archive.read(f"lib/{compat}/compat-target.txt").decode().strip()
            except UnicodeDecodeError as exc:
                raise ReleaseError(f"{path.name} has a non-text compatibility marker for STS2 {compat}") from exc
            if marker != compat:
                raise ReleaseError(f"{path.name} has an invalid compatibility marker for STS2 {compat}")
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
    if is_ritsulib_assembly(path):
        return False
    return True
