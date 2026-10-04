import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from package_client_bundle import assemble
from client_archive import SUPPORTED_STS2_API_COMPATS


PUBLIC_API, BETA_API = SUPPORTED_STS2_API_COMPATS


class PackageClientBundleTests(unittest.TestCase):
    def test_local_and_package_bundles(self):
        for local in (True, False):
            with self.subTest(local=local), tempfile.TemporaryDirectory() as temporary:
                repo = Path(temporary) / "repo with spaces"
                output = repo / "game/mods/Archipelago"
                output.mkdir(parents=True)
                (output / "Archipelago.pck").write_bytes(b"pack")
                (output / "Archipelago.json").write_text('{"version": "2.4.0"}')
                (output / "user-file.txt").write_text("keep")
                if not local:
                    with self.assertRaisesRegex(ValueError, "spire2.apworld"), \
                            patch("package_client_bundle.subprocess.run") as run:
                        assemble(repo, output, "Debug", PUBLIC_API, BETA_API)
                    run.assert_not_called()
                    (output / "spire2.apworld").write_bytes(b"world")

                for version in SUPPORTED_STS2_API_COMPATS:
                    source = repo / "client/StS2AP/bin" / version / "Debug/net9.0"
                    (source / "data").mkdir(parents=True)
                    (source / "Archipelago.dll").write_bytes(version.encode())
                    (source / "data/relic_custom_pools.data").write_bytes(b"catalog")
                    for name in ("StS2AP.Domain.dll", "FSharp.Core.dll", "sts2.dll",
                                 "0Harmony.dll", "GodotSharp.dll", "STS2.RitsuLib.dll"):
                        (source / name).write_bytes(b"dependency")
                loader = repo / "client/StS2AP.Loader/bin/Debug/net9.0/Archipelago.Loader.dll"
                loader.parent.mkdir(parents=True)
                loader.write_bytes(b"loader")

                with patch("package_client_bundle.subprocess.run") as run:
                    assemble(repo, output, "Debug", PUBLIC_API, BETA_API, local=local)
                self.assertEqual(run.call_count, 3)
                for index, call in enumerate(run.call_args_list):
                    command = call.args[0]
                    self.assertTrue(call.kwargs["check"])
                    self.assertEqual("-p:UseSts2RefLib=true" in command, not local)
                    if index < 2:
                        self.assertIn("-p:BuildMode=CompileOnly", command)
                manifest = json.loads((output / "archipelago-variants.manifest").read_text())
                self.assertEqual(set(manifest["variants"]), set(SUPPORTED_STS2_API_COMPATS))
                for version, entry in manifest["variants"].items():
                    assembly = (output / entry["assembly"]).read_bytes()
                    self.assertEqual(assembly, version.encode())
                    self.assertEqual(entry["sha256"], hashlib.sha256(assembly).hexdigest())
                self.assertEqual((output / "Archipelago.dll").read_bytes(), b"loader")
                self.assertTrue((output / "StS2AP.Domain.dll").is_file())
                self.assertTrue((output / "FSharp.Core.dll").is_file())
                self.assertEqual((output / "data/relic_custom_pools.data").read_bytes(), b"catalog")
                self.assertEqual((output / "user-file.txt").read_text(), "keep")
                for excluded in ("sts2.dll", "0Harmony.dll", "GodotSharp.dll", "STS2.RitsuLib.dll"):
                    self.assertFalse((output / excluded).exists())


if __name__ == "__main__":
    unittest.main()
