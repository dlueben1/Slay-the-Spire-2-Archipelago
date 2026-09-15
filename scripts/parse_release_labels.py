#!/usr/bin/env python3
"""Validate merged-PR release labels and emit the requested SemVer bumps."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Iterable, TextIO


CLIENT_PREFIX = "release-client-"
WORLD_PREFIX = "release-world-"
BUMPS = {"major", "minor", "patch"}


class LabelError(ValueError):
    """Raised when release labels are ambiguous or invalid."""


def parse_labels(labels: Iterable[str]) -> dict[str, str]:
    label_set = set(labels)
    client = sorted(
        label for label in label_set if label in {f"{CLIENT_PREFIX}{bump}" for bump in BUMPS}
    )
    world = sorted(
        label for label in label_set if label in {f"{WORLD_PREFIX}{bump}" for bump in BUMPS}
    )

    if len(client) > 1:
        raise LabelError(
            "Conflicting client release labels: " + ", ".join(client)
        )
    if len(world) > 1:
        raise LabelError(
            "Conflicting APWorld release labels: " + ", ".join(world)
        )
    if world and not client:
        raise LabelError(
            f"{world[0]} requires exactly one release-client-* label."
        )

    if not client:
        return {
            "should_release": "false",
            "client_bump": "none",
            "world_bump": "none",
        }

    return {
        "should_release": "true",
        "client_bump": client[0].removeprefix(CLIENT_PREFIX),
        "world_bump": (
            world[0].removeprefix(WORLD_PREFIX) if world else "none"
        ),
    }


def write_outputs(outputs: dict[str, str], destination: TextIO) -> None:
    for key, value in outputs.items():
        destination.write(f"{key}={value}\n")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--labels-json", required=True)
    parser.add_argument("--github-output", type=Path, required=True)
    args = parser.parse_args()

    labels = json.loads(args.labels_json)
    if not isinstance(labels, list) or not all(isinstance(label, str) for label in labels):
        parser.error("--labels-json must be a JSON array of strings")

    try:
        outputs = parse_labels(labels)
    except LabelError as error:
        parser.error(str(error))

    with args.github_output.open("a", encoding="utf-8", newline="\n") as destination:
        write_outputs(outputs, destination)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
