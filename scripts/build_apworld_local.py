#!/usr/bin/env python3
"""Build this checkout's live spire2 world with the sibling Archipelago source."""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path


def main() -> None:
    repo = Path(__file__).resolve().parents[1]
    archipelago = repo.parent / "Archipelago"
    world_source = repo / "world" / "spire2"
    installed_world = archipelago / "worlds" / "spire2"

    if not (archipelago / "Launcher.py").is_file():
        raise SystemExit(f"Archipelago 0.6.7 checkout not found at {archipelago}")
    if not installed_world.exists():
        installed_world.symlink_to(world_source, target_is_directory=True)
    if installed_world.resolve() != world_source:
        raise SystemExit(f"{installed_world} must link to {world_source}")

    environment = os.environ.copy()
    environment["SKIP_REQUIREMENTS_UPDATE"] = "true"
    environment["PYTHONPATH"] = os.pathsep.join(
        path for path in (str(archipelago), environment.get("PYTHONPATH", "")) if path
    )
    for kind in ("cache", "config", "data"):
        environment.setdefault(f"XDG_{kind.upper()}_HOME", str(repo / ".local-tools" / kind))

    # A read-only sibling checkout has no generated Players/manifest directories.
    # Keep Archipelago's user data inside this writable checkout for the build.
    user_data = repo / ".local-tools" / "data" / "Archipelago"
    user_data.mkdir(parents=True, exist_ok=True)
    build_root = repo / ".local-tools" / "apworld-build"
    build_root.mkdir(parents=True, exist_ok=True)
    build_worlds = build_root / "worlds"
    if not build_worlds.exists():
        build_worlds.symlink_to(archipelago / "worlds", target_is_directory=True)
    if build_worlds.resolve() != (archipelago / "worlds").resolve():
        raise SystemExit(f"{build_worlds} must link to {archipelago / 'worlds'}")

    build_code = (
        f"import Utils; Utils.user_path.cached_path = {str(user_data)!r}; "
        "import Launcher; "
        "from worlds.LauncherComponents import _build_apworlds; "
        "Launcher.open_folder = lambda *_: None; "
        '_build_apworlds("Slay the Spire II")'
    )
    subprocess.run([sys.executable, "-c", build_code], cwd=build_root, env=environment, check=True)

    source = build_root / "build" / "apworlds" / "spire2.apworld"
    destination = repo / "dist" / "spire2.apworld"
    destination.parent.mkdir(exist_ok=True)
    shutil.copy2(source, destination)
    print(f"Built {destination}")


if __name__ == "__main__":
    main()
