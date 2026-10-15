#!/usr/bin/env python3
"""Launch two isolated native Linux StS2 beta clients for AP multiplayer tests."""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import shlex
import subprocess
import sys
import time
from xml.etree import ElementTree


REPO_ROOT = Path(__file__).resolve().parents[1]


def resolve_executable(repo: Path, explicit: Path | None) -> Path:
    if explicit is not None:
        candidates = [explicit.expanduser()]
    else:
        candidates = []
        props = repo / "client/StS2AP/local.props"
        if props.is_file():
            try:
                game_path = ElementTree.parse(props).findtext(".//STS2GamePath", "").strip()
                # This is path discovery, not an MSBuild evaluator.
                if game_path and "$(" not in game_path:
                    configured = Path(os.path.expandvars(game_path)).expanduser()
                    if configured.is_absolute():
                        candidates.append(configured / "SlayTheSpire2")
            except (OSError, ElementTree.ParseError) as error:
                print(f"Warning: could not read {props}: {error}", file=sys.stderr)
        data_home = Path(os.environ.get("XDG_DATA_HOME", Path.home() / ".local/share"))
        for steam in (data_home / "Steam", Path.home() / ".local/share/Steam",
                      Path.home() / ".steam/steam", Path.home() / ".steam/root"):
            candidates.append(steam / "steamapps/common/Slay the Spire 2/SlayTheSpire2")
    for candidate in candidates:
        if candidate.is_file() and os.access(candidate, os.X_OK):
            return candidate.resolve()
    raise ValueError("Could not find an executable native SlayTheSpire2. Set an absolute "
                     "STS2GamePath in client/StS2AP/local.props or pass --exe-path PATH.")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    parser.add_argument("--exe-path", "-ExePath", type=Path)
    parser.add_argument("--ap-server", "-ApServer", default="localhost:38281")
    parser.add_argument("--host-slot", "-HostSlot", default="Alice")
    parser.add_argument("--client-slot", "-ClientSlot", default="Bob")
    parser.add_argument("--host-client-id", "-HostClientId", type=int, default=1)
    parser.add_argument("--client-client-id", "-ClientClientId", type=int, default=1000)
    parser.add_argument("--launch-delay-seconds", "-LaunchDelaySeconds", type=int, default=2)
    parser.add_argument("--settings-only", "-SettingsOnly", action="store_true",
                        help="open both accounts to enable Experimental Multiplayer first")
    parser.add_argument("--dry-run", "-DryRun", action="store_true",
                        help="print the commands without launching or writing files")
    return parser


def launch_commands(args: argparse.Namespace, executable: Path, logs: Path) -> list[list[str]]:
    if args.host_client_id == args.client_client_id:
        raise ValueError("HostClientId and ClientClientId must be different.")
    if any(not 1 <= value <= 2147483647 for value in (args.host_client_id, args.client_client_id)):
        raise ValueError("Client IDs must be between 1 and 2147483647.")
    if not 0 <= args.launch_delay_seconds <= 30:
        raise ValueError("LaunchDelaySeconds must be between 0 and 30.")
    if any(not value.strip() for value in (args.ap_server, args.host_slot, args.client_slot)):
        raise ValueError("AP server and slot names must not be empty.")
    commands = []
    for role, client_id, slot in (("host_standard", args.host_client_id, args.host_slot),
                                  ("join", args.client_client_id, args.client_slot)):
        command = [str(executable), "--log-file", str(logs / f"{role}-{client_id}.log"),
                   "-force-steam", "off", "-clientId", str(client_id)]
        if not args.settings_only:
            command += ["-fastmp", "-apFastmp", role, "-apServer", args.ap_server,
                        "-apSlot", slot, "-apHostSlot", args.host_slot,
                        "-apClientSlot", args.client_slot,
                        "-apHostClientId", str(args.host_client_id),
                        "-apClientClientId", str(args.client_client_id)]
        commands.append(command)
    return commands


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    if sys.platform != "linux":
        parser.error("Use test_multiplayer_local.ps1 on Windows.")
    try:
        executable = resolve_executable(REPO_ROOT, args.exe_path)
        logs = REPO_ROOT / "logs/multiplayer"
        commands = launch_commands(args, executable, logs)
        print(f"Working directory: {executable.parent}")
        for command in commands:
            print(shlex.join(command))
        if args.dry_run:
            return 0

        logs.mkdir(parents=True, exist_ok=True)
        processes = []
        for index, command in enumerate(commands):
            if index:
                time.sleep(args.launch_delay_seconds)
                if processes[0].poll() is not None:
                    raise ValueError("Host exited before the client launched. Check logs/multiplayer.")
            # Detach from the shell; each process keeps its own game and console logs.
            console_log = Path(command[2]).with_suffix(".console.log")
            with console_log.open("w", encoding="utf-8") as console:
                process = subprocess.Popen(command, cwd=executable.parent,
                                           stdin=subprocess.DEVNULL, stdout=console,
                                           stderr=subprocess.STDOUT, start_new_session=True)
            processes.append(process)
            print(f"Started PID {process.pid}; console log: {console_log}", flush=True)
        if args.settings_only:
            print("Enable Experimental Multiplayer in Archipelago Settings in both windows, "
                  "close them, then rerun without --settings-only.")
        else:
            print(f"Connect {args.host_slot} first. After its native lobby opens, connect {args.client_slot}.")
    except (OSError, ValueError) as error:
        parser.error(str(error))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
