from __future__ import annotations

from contextlib import redirect_stdout
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

from scripts import test_multiplayer_local as launcher


@unittest.skipUnless(sys.platform == "linux", "native Linux launcher")
class LinuxMultiplayerLauncherTests(unittest.TestCase):
    def make_executable(self, root: Path) -> Path:
        executable = root / "Steam library/Slay the Spire 2/SlayTheSpire2"
        executable.parent.mkdir(parents=True)
        executable.write_text(
            "#!/usr/bin/env python3\n"
            "import json, os, pathlib, sys, time\n"
            "log = pathlib.Path(sys.argv[sys.argv.index('--log-file') + 1])\n"
            "log.write_text(json.dumps({'argv': sys.argv[1:], 'cwd': os.getcwd()}))\n"
            "print('fake game started', flush=True)\n"
            "time.sleep(0.5)\n"
        )
        executable.chmod(0o755)
        return executable

    def test_launches_two_real_processes_with_isolated_ids_and_literal_arguments(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            executable = self.make_executable(root)
            # Shell metacharacters and spaces must reach the game as one slot value.
            host_slot = "Alice & $(not-a-command)"
            processes = []
            popen = subprocess.Popen

            def start(*args, **kwargs):
                process = popen(*args, **kwargs)
                processes.append(process)
                return process

            try:
                with patch.object(launcher, "REPO_ROOT", root), \
                     patch.object(launcher.subprocess, "Popen", side_effect=start), \
                     redirect_stdout(io.StringIO()):
                    result = launcher.main([
                        "--exe-path", str(executable), "--launch-delay-seconds", "0",
                        "--host-slot", host_slot, "--client-slot", "Bob Smith",
                    ])
                for process in processes:
                    self.assertEqual(process.wait(timeout=5), 0)
                self.assertEqual(result, 0)
                self.assertEqual(len(processes), 2)
                for role, client_id, slot in (("host_standard", "1", host_slot),
                                              ("join", "1000", "Bob Smith")):
                    log = root / "logs/multiplayer" / f"{role}-{client_id}.log"
                    record = json.loads(log.read_text())
                    arguments = record["argv"]
                    self.assertEqual(record["cwd"], str(executable.parent))
                    for flag, value in (("-clientId", client_id), ("-apSlot", slot),
                                        ("-apFastmp", role), ("-force-steam", "off"),
                                        ("-apHostClientId", "1"), ("-apClientClientId", "1000")):
                        self.assertEqual(arguments[arguments.index(flag) + 1], value)
                    self.assertEqual(arguments[arguments.index("-fastmp") + 1], "-apFastmp")
                    self.assertIn("fake game started", log.with_suffix(".console.log").read_text())
                self.assertFalse((executable.parent / "steam_appid.txt").exists())
            finally:
                for process in processes:
                    if process.poll() is None:
                        process.terminate()
                    process.wait(timeout=5)

    def test_dry_run_settings_only_has_no_process_or_file_side_effects(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            executable = self.make_executable(root)
            output = io.StringIO()
            with patch.object(launcher, "REPO_ROOT", root), \
                 patch.object(launcher.subprocess, "Popen") as popen, redirect_stdout(output):
                self.assertEqual(launcher.main([
                    "-ExePath", str(executable), "-SettingsOnly", "-DryRun",
                ]), 0)
            popen.assert_not_called()
            self.assertFalse((root / "logs").exists())
            self.assertNotIn("-fastmp", output.getvalue())
            self.assertIn("-clientId 1", output.getvalue())
            self.assertIn("-clientId 1000", output.getvalue())

    def test_discovers_native_executable_from_local_props(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            executable = self.make_executable(root)
            props = root / "client/StS2AP/local.props"
            props.parent.mkdir(parents=True)
            props.write_text(f"<Project><PropertyGroup><STS2GamePath>{executable.parent}"
                             "</STS2GamePath></PropertyGroup></Project>")
            self.assertEqual(launcher.resolve_executable(root, None), executable)

    def test_discovers_xdg_steam_install(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            executable = root / "Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2"
            executable.parent.mkdir(parents=True)
            executable.touch(mode=0o755)
            with patch.dict(os.environ, {"XDG_DATA_HOME": str(root)}):
                self.assertEqual(launcher.resolve_executable(root, None), executable)

    def test_invalid_explicit_path_does_not_fall_back_to_installed_game(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with self.assertRaisesRegex(ValueError, "Could not find"):
                launcher.resolve_executable(root, root / "missing")
            not_executable = root / "SlayTheSpire2"
            not_executable.touch(mode=0o644)
            with self.assertRaisesRegex(ValueError, "Could not find"):
                launcher.resolve_executable(root, not_executable)

    def test_invalid_identity_and_delay_options_fail_before_launch(self):
        for options in (["-ClientClientId", "1"], ["-HostClientId", "0"],
                        ["-ClientClientId", "2147483648"], ["-LaunchDelaySeconds", "31"],
                        ["-ApServer", ""], ["-HostSlot", " "]):
            with self.subTest(options=options), self.assertRaises(ValueError):
                launcher.launch_commands(launcher.build_parser().parse_args(options),
                                         Path("/game/SlayTheSpire2"), Path("/logs"))


if __name__ == "__main__":
    unittest.main()
