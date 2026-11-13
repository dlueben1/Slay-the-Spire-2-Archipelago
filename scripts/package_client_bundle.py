#!/usr/bin/env python3
"""Assemble the loader and every client variant after exporting the PCK."""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
from pathlib import Path


def assemble(repo: Path, output: Path, configuration: str, main: str, beta: str,
             *, extra: tuple[str, ...] = (), local: bool = False) -> None:
    output = output.resolve()
    versions = (main, *extra, beta)
    required_files = ["Archipelago.pck", "Archipelago.json"]
    if not local:
        required_files.append("spire2.apworld")
    for required in required_files:
        if not (output / required).is_file():
            raise ValueError(f"Package staging is missing {required}: {output}")
    # Local builds use the same reference settings as the IDE via local.props.
    reference_properties = [] if local else ["-p:UseSts2RefLib=true"]
    project = repo / "client/StS2AP/StS2AP.csproj"
    for version in versions:
        subprocess.run([
            "dotnet", "build", str(project), "-c", configuration,
            "-p:BuildMode=CompileOnly", *reference_properties,
            f"-p:Sts2ApiCompat={version}",
        ], check=True)
    subprocess.run([
        "dotnet", "build", str(repo / "client/StS2AP.Loader/StS2AP.Loader.csproj"),
        "-c", configuration, *reference_properties, f"-p:Sts2ApiCompat={main}",
    ], check=True)

    manifest = {
        "schema": 1,
        "modVersion": json.loads((output / "Archipelago.json").read_text(encoding="utf-8-sig"))["version"],
        "variants": {},
    }
    for version in versions:
        source = repo / "client/StS2AP/bin" / version / configuration / "net9.0"
        destination = output / "lib" / version
        destination.mkdir(parents=True, exist_ok=True)
        assembly = destination / "Archipelago.dll"
        shutil.copy2(source / "Archipelago.dll", assembly)
        (destination / "compat-target.txt").write_text(version + "\n", encoding="utf-8")
        manifest["variants"][version] = {
            "assembly": f"lib/{version}/Archipelago.dll",
            "sha256": hashlib.sha256(assembly.read_bytes()).hexdigest(),
        }
    dependencies = repo / "client/StS2AP/bin" / beta / configuration / "net9.0"
    for dll in dependencies.glob("*.dll"):
        if dll.name in {"Archipelago.dll", "sts2.dll", "GodotSharp.dll", "0Harmony.dll"}:
            continue
        if dll.name.lower().startswith(("sts2.ritsulib", "sts2-ritsulib")):
            continue
        shutil.copy2(dll, output / dll.name)
    shutil.copytree(dependencies / "data", output / "data", dirs_exist_ok=True)
    for dependency in ("StS2AP.Domain.dll", "FSharp.Core.dll"):
        if not (output / dependency).is_file():
            raise ValueError(f"Package is missing required dependency {dependency}")
    loader = repo / "client/StS2AP.Loader/bin" / configuration / "net9.0/Archipelago.Loader.dll"
    shutil.copy2(loader, output / "Archipelago.dll")
    (output / "archipelago-variants.manifest").write_text(
        json.dumps(manifest, indent=2) + "\n", encoding="utf-8"
    )


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--main", required=True)
    parser.add_argument("--beta", required=True)
    parser.add_argument("--extra", default="",
                        help="Other game versions to build, separated by semicolons.")
    parser.add_argument("--local", action="store_true",
                        help="Use local reference settings and allow a missing APWorld.")
    args = parser.parse_args()
    assemble(Path(__file__).resolve().parents[1], args.output,
             args.configuration, args.main, args.beta,
             extra=tuple(version.strip() for version in args.extra.split(";") if version.strip()),
             local=args.local)
