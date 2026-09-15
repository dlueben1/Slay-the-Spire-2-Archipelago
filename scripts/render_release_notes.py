#!/usr/bin/env python3
"""Render the GitHub Release notes template with release-specific versions."""

from __future__ import annotations

import argparse
import re
from pathlib import Path


PLACEHOLDER = re.compile(r"{{[A-Z0-9_]+}}")


def render_notes(template: str, values: dict[str, str]) -> str:
    rendered = template
    for name, value in values.items():
        placeholder = "{{" + name + "}}"
        if placeholder not in rendered:
            raise ValueError(f"Required placeholder {placeholder} is missing.")
        rendered = rendered.replace(placeholder, value)

    unresolved = sorted(set(PLACEHOLDER.findall(rendered)))
    if unresolved:
        raise ValueError("Unresolved placeholders: " + ", ".join(unresolved))
    return rendered


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--template", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--client-version", required=True)
    parser.add_argument("--world-version", required=True)
    parser.add_argument("--sts2-public-version", required=True)
    parser.add_argument("--sts2-beta-version", required=True)
    args = parser.parse_args()

    values = {
        "CLIENT_VERSION": args.client_version,
        "WORLD_VERSION": args.world_version,
        "STS2_PUBLIC_VERSION": args.sts2_public_version,
        "STS2_BETA_VERSION": args.sts2_beta_version,
    }

    try:
        template = args.template.read_bytes().decode("utf-8")
        rendered = render_notes(template, values)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_bytes(rendered.encode("utf-8"))
    except (OSError, UnicodeError, ValueError) as error:
        parser.error(str(error))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
