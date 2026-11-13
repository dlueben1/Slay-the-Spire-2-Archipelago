#!/usr/bin/env python3
"""Report what a game update means for the client, without launching the game.

Run it once the new game's DLLs are in <references>/<version>/. See scripts/README.md.
"""

from __future__ import annotations

import argparse
import difflib
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
CLIENT_PROJECT = REPO / "client/StS2AP/StS2AP.csproj"
TOOL_PROJECT = REPO / "client/StS2AP.UpdateCheck/StS2AP.UpdateCheck.csproj"

COMPILE_ERROR = re.compile(r"^(?P<file>.+?)\((?P<line>\d+),\d+\): error (?P<code>CS\d+): (?P<message>.+?)(?: \[.*\])?$")
VERSION_SYMBOL = re.compile(r"STS2_AT_LEAST_(\d+)_(\d+)_(\d+)")


def client_build_command(version: str, references: Path) -> list[str]:
    # A version the repository doesn't support yet passes the project's check as the beta version.
    return [
        "dotnet", "build", str(CLIENT_PROJECT), "-v:q", "-nologo",
        "-p:BuildMode=CompileOnly", "-p:UseSts2RefLib=false",
        f"-p:Sts2ApiSignatureRoot={references}",
        f"-p:Sts2ApiCompat={version}", f"-p:Sts2BetaApiCompat={version}",
    ]


def find_game_data(local_props: Path) -> Path | None:
    """The installed game's data folder, which holds the DLLs the references folder leaves out."""
    if not local_props.is_file():
        return None
    match = re.search(r"<STS2GamePath>(.+?)</STS2GamePath>", local_props.read_text(encoding="utf-8-sig"))
    return next(iter(sorted(Path(match[1]).glob("data_sts2_*"))), None) if match else None


def run_tool(step: str, *arguments: object) -> None:
    result = subprocess.run(
        ["dotnet", "run", "--project", str(TOOL_PROJECT), "-c", "Release", "-v:q", "--",
         step, *map(str, arguments)],
        capture_output=True, text=True,
    )
    if result.returncode != 0:
        raise RuntimeError(f"The {step} step failed:\n{result.stdout}{result.stderr}")


def parse_compile_errors(output: str) -> list[str]:
    """Each distinct compiler error once, with paths relative to the repository."""
    errors = []
    for line in output.splitlines():
        match = COMPILE_ERROR.match(line.strip())
        if match:
            file = match["file"].replace("\\", "/").removeprefix(REPO.as_posix() + "/")
            errors.append(f"{file}:{match['line']}: {match['code']}: {match['message']}")
    return sorted(set(errors))


def version_branches(repo: Path) -> dict[tuple[int, ...], list[str]]:
    """Every use of a version symbol in the client's C# code, by version."""
    branches: dict[tuple[int, ...], list[str]] = {}
    for path in sorted((repo / "client/StS2AP").rglob("*.cs")):
        if {"bin", "obj"} & set(path.parts):
            continue
        for number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
            if line.lstrip().startswith("#"):
                for match in VERSION_SYMBOL.finditer(line):
                    version = tuple(int(part) for part in match.groups())
                    branches.setdefault(version, []).append(f"{path.relative_to(repo).as_posix()}:{number}")
    return branches


def supported_versions(props: str) -> list[tuple[int, ...]]:
    # Sts2ExtraApiCompats holds a list, separated by semicolons.
    found = [
        version.strip()
        for value in re.findall(r"<Sts2\w+ApiCompats?>([\d.;\s]+)</", props)
        for version in value.split(";")
        if version.strip()
    ]
    return [tuple(int(part) for part in version.split(".")) for version in found]


def short_name(name: str) -> str:
    """Keep the class and method, drop namespaces, and spell generics the C# way."""
    method, parenthesis, parameters = name.partition("(")
    parameters = re.sub(r"(?:\w+\.)+(?=\w)", "", parameters)
    previous = None
    while parameters != previous:
        previous = parameters
        parameters = re.sub(r"`\d+\[\[([^\[\]]*)\]\]", r"<\1>", parameters)
    return ".".join(method.split(".")[-2:]) + parenthesis + parameters


def changed_size(method: dict) -> int:
    return sum(
        line[0] in "+-"
        for line in difflib.ndiff(method["previous"].splitlines(), method["current"].splitlines())
    )


def method_diff(method: dict) -> str:
    return "\n".join(difflib.unified_diff(
        method["previous"].splitlines(), method["current"].splitlines(),
        "previous", "current", lineterm="", n=2,
    ))


def render_report(old: str, new: str, check: dict, compile_errors: list[str],
                  branches: dict[tuple[int, ...], list[str]], supported: list[tuple[int, ...]]) -> str:
    methods = check["patchedMethods"]
    changed = sorted(
        (method for method in methods if method["status"] == "changed"),
        key=changed_size, reverse=True,
    )
    unmatched = [method["name"] for method in methods if method["status"] == "new"]
    failures = check["failures"]

    lines = [
        f"# Game update report: {old} to {new}",
        "",
        f"- Patch classes that no longer attach: **{len(failures)}** of "
        f"{check['appliedClasses'] + len(failures)}",
        f"- Compile errors against {new}: **{len(compile_errors)}**",
        f"- Patched game methods to read: **{len(changed)}** of {len(methods)}",
        "",
        f"## Patches from the {old} build that don't attach to {new}",
        "",
    ]
    lines += [f"- `{failure['patchClass']}`: {failure['error']}" for failure in failures] or ["None."]
    if check["typeLoadErrors"]:
        lines += ["", "Client types that couldn't load:", ""]
        lines += [f"- {error}" for error in check["typeLoadErrors"]]

    lines += ["", f"## Compile errors against {new}", ""]
    if compile_errors:
        lines += [f"- `{error}`" for error in compile_errors]
        lines += ["", "Rerun after fixing these: errors inside method bodies only appear once "
                      "declarations and attributes compile."]
    else:
        lines.append("None.")

    lines += ["", "## Patched game methods that changed", "",
              "These still attach, but may behave differently. Largest change first.", ""]
    lines += [
        f"- `{short_name(method['name'])}` ({changed_size(method)} changed lines)" for method in changed
    ] or ["None."]
    if unmatched:
        lines += ["", f"Patched in {new} with no method of that name in {old}:", ""]
        lines += [f"- `{short_name(name)}`" for name in unmatched]

    lines += ["", "## Game content added or removed", "",
              "Check these against the hard-coded lists in the update checklist.", ""]
    for change in check["contentChanges"]:
        added = ", ".join(f"`{name.rpartition('.')[2]}`" for name in change["added"]) or "none"
        removed = ", ".join(f"`{name.rpartition('.')[2]}`" for name in change["removed"]) or "none"
        lines.append(f"- **{change['category']}** added: {added}; removed: {removed}")
    if not check["contentChanges"]:
        lines.append("None.")

    lines += ["", "## Version branches in the client", ""]
    for version, sites in sorted(branches.items()):
        name = ".".join(str(part) for part in version)
        settled = supported and min(supported) >= version
        note = " (true on every supported version, so it can be made unconditional)" if settled else ""
        lines.append(f"- **{name}**{note}: " + ", ".join(f"`{site}`" for site in sites))
    if not branches:
        lines.append("None.")

    lines += ["", "## Diffs of the changed methods", ""]
    for method in changed:
        lines += [f"### `{short_name(method['name'])}`", "", "```diff", method_diff(method), "```", ""]
    return "\n".join(lines).rstrip() + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--old", required=True, help="game version the client currently builds against")
    parser.add_argument("--new", required=True, help="game version to check")
    parser.add_argument("--references", type=Path, default=REPO / ".local-tools/sts2-references",
                        help="folder holding <version>/sts2.dll, 0Harmony.dll and GodotSharp.dll")
    parser.add_argument("--game-data", type=Path,
                        help="installed game's data_sts2_* folder; defaults to the one under "
                             "STS2GamePath in client/StS2AP/local.props")
    parser.add_argument("--output", type=Path, help="report path; defaults to dist/")
    args = parser.parse_args()

    references = args.references.resolve()
    for version in (args.old, args.new):
        if not (references / version / "sts2.dll").is_file():
            parser.error(f"{references / version / 'sts2.dll'} doesn't exist")
    game_data = args.game_data or find_game_data(REPO / "client/StS2AP/local.props")
    if not game_data or not (game_data / "sts2.dll").is_file():
        parser.error("can't find the installed game's data folder; pass --game-data")
    output = args.output or REPO / f"dist/game-update-{args.old}-to-{args.new}.md"

    print(f"Building the client against {args.old}...", flush=True)
    old_build = subprocess.run(client_build_command(args.old, references), capture_output=True, text=True)
    if old_build.returncode != 0:
        print(old_build.stdout + old_build.stderr, file=sys.stderr)
        return 2

    print(f"Checking patches against {args.new} and comparing the game versions...", flush=True)
    new_game = references / args.new
    with tempfile.TemporaryDirectory() as temporary:
        attach_file, compare_file = Path(temporary) / "attach.json", Path(temporary) / "compare.json"
        run_tool("attach",
                 "--mod", REPO / "client/StS2AP/bin" / args.old / "Debug/net9.0",
                 "--game", new_game, "--game-data", game_data, "--output", attach_file)
        run_tool("compare",
                 "--game", new_game, "--previous-game", references / args.old, "--game-data", game_data,
                 "--patched", attach_file, "--output", compare_file)
        check = json.loads(attach_file.read_text(encoding="utf-8")) \
            | json.loads(compare_file.read_text(encoding="utf-8"))

    print(f"Building the client against {args.new}...", flush=True)
    new_build = subprocess.run(client_build_command(args.new, references), capture_output=True, text=True)
    compile_errors = parse_compile_errors(new_build.stdout + new_build.stderr)
    if new_build.returncode != 0 and not compile_errors:
        print(new_build.stdout + new_build.stderr, file=sys.stderr)
        return 2

    report = render_report(
        args.old, args.new, check, compile_errors,
        version_branches(REPO),
        supported_versions((REPO / "client/StS2AP/SupportedGameVersions.props").read_text(encoding="utf-8")),
    )
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(report, encoding="utf-8")
    print("\n".join(report.splitlines()[2:5]))
    print(f"Report: {output}")
    return 1 if check["failures"] or compile_errors else 0


if __name__ == "__main__":
    sys.exit(main())
