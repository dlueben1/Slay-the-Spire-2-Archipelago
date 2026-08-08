<#
Generate JSON metadata for the Vue YAML builder from the runtime Python world code.
Reads world/spire2/options.py and world/spire2/web_world.py.
Writes web/src/generated/options_compiled.json.

Run:
    powershell -ExecutionPolicy Bypass -File .\scripts\generate_options_for_web.ps1
#>

param(
    [string]$PythonExecutable = "python"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..") | Select-Object -ExpandProperty Path
$OptionsPath = Join-Path $RepoRoot "world\spire2\options.py"
$WebWorldPath = Join-Path $RepoRoot "world\spire2\web_world.py"
$OutPath = Join-Path $RepoRoot "web\src\generated\options_compiled.json"
$OutDirectory = Split-Path -Parent $OutPath

$RepoParent = Split-Path -Parent $RepoRoot
$ArchipelagoRoot = $null
$ArchipelagoCandidates = @(
    (Join-Path $RepoParent "Archipelago"),
    (Join-Path $RepoRoot "..\Archipelago")
)

foreach ($candidate in $ArchipelagoCandidates) {
    if (Test-Path (Join-Path $candidate "Options.py") -PathType Leaf) {
        $ArchipelagoRoot = (Resolve-Path $candidate | Select-Object -ExpandProperty Path)
        break
    }
}

if (-not $ArchipelagoRoot) {
    throw "Could not find the Archipelago source checkout containing Options.py near $RepoRoot"
}

if (-not (Test-Path $OptionsPath -PathType Leaf)) {
    throw "Could not find options.py at $OptionsPath"
}

if (-not (Test-Path $WebWorldPath -PathType Leaf)) {
    throw "Could not find web_world.py at $WebWorldPath"
}

$dependencyCheckScript = @'
import importlib.util
import sys

required_modules = ["schema", "typing_extensions", "pathspec", "yaml"]
missing = [name for name in required_modules if importlib.util.find_spec(name) is None]
sys.exit(0 if not missing else 1)
'@

& $PythonExecutable -c $dependencyCheckScript | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Installing missing Python dependencies required by Archipelago..." -ForegroundColor Yellow
    & $PythonExecutable -m pip install --disable-pip-version-check --no-warn-script-location schema typing_extensions pathspec pyyaml
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install required Python dependencies for Archipelago."
    }
}

New-Item -ItemType Directory -Path $OutDirectory -Force | Out-Null

$py = @'
import hashlib
import importlib.util
import inspect
import json
import re
import sys
from dataclasses import fields
from pathlib import Path
from typing import get_type_hints

repo_root = Path(sys.argv[1]).resolve()
out_path = Path(sys.argv[2]).resolve()
archipelago_root = Path(sys.argv[3]).resolve() if len(sys.argv) > 3 else None
world_dir = repo_root / "world" / "spire2"
options_path = world_dir / "options.py"
web_world_path = world_dir / "web_world.py"

for required_path in (options_path, web_world_path):
    if not required_path.is_file():
        raise FileNotFoundError(f"Required source file does not exist: {required_path}")

# Make the repository's Archipelago modules (Options, BaseClasses, worlds, etc.)
# available before loading this APWorld's files under their runtime package names.
repo_root_string = str(repo_root)
if repo_root_string not in sys.path:
    sys.path.insert(0, repo_root_string)

if archipelago_root is not None:
    archipelago_root_string = str(archipelago_root)
    if archipelago_root_string not in sys.path:
        sys.path.insert(0, archipelago_root_string)

# web_world.py only needs a small subset of the Archipelago runtime for this metadata export.
# Stub the minimal modules that would otherwise trigger the full world registry import chain.
import types

base_classes_module = types.ModuleType("BaseClasses")
class Tutorial:
    def __init__(self, *args, **kwargs):
        self.args = args
        self.kwargs = kwargs

base_classes_module.Tutorial = Tutorial
sys.modules["BaseClasses"] = base_classes_module

worlds_package = types.ModuleType("worlds")
worlds_package.__path__ = []
sys.modules["worlds"] = worlds_package

auto_world_module = types.ModuleType("worlds.AutoWorld")
class WebWorld:
    pass

auto_world_module.WebWorld = WebWorld
sys.modules["worlds.AutoWorld"] = auto_world_module
worlds_package.AutoWorld = auto_world_module

spire2_package = types.ModuleType("worlds.spire2")
spire2_package.__path__ = []
sys.modules["worlds.spire2"] = spire2_package


def load_module_as(name: str, path: Path):
    """Load a source file under the module name used by the Archipelago runtime."""
    spec = importlib.util.spec_from_file_location(name, str(path))
    if spec is None or spec.loader is None:
        raise ImportError(f"Could not create an import specification for {path}")

    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


# options.py uses relative imports, so load its local dependencies first.
load_module_as("worlds.spire2.characters", world_dir / "characters.py")
load_module_as("worlds.spire2.constants", world_dir / "constants.py")
options_module = load_module_as("worlds.spire2.options", options_path)
web_world_module = load_module_as("worlds.spire2.web_world", web_world_path)

import Options as ap_options


OPTION_BASE_CLASSES = {
    name: getattr(ap_options, name, None)
    for name in (
        "Option",
        "Toggle",
        "TextChoice",
        "NamedRange",
        "Range",
        "Choice",
        "OptionCounter",
        "OptionSet",
        "OptionList",
        "OptionDict",
        "FreeText",
        "NumericOption",
    )
}


def is_subclass(option_type: type, base_name: str) -> bool:
    base_type = OPTION_BASE_CLASSES.get(base_name)
    return isinstance(base_type, type) and issubclass(option_type, base_type)


def normalize_json(value):
    """Convert common AP option values into deterministic JSON-compatible values."""
    if value is None or isinstance(value, (str, int, float, bool)):
        return value

    if isinstance(value, dict):
        return {
            str(key): normalize_json(child_value)
            for key, child_value in value.items()
        }

    if isinstance(value, (list, tuple)):
        return [normalize_json(child_value) for child_value in value]

    if isinstance(value, (set, frozenset)):
        normalized = [normalize_json(child_value) for child_value in value]
        return sorted(normalized, key=lambda item: str(item).casefold())

    enum_name = getattr(value, "name", None)
    enum_value = getattr(value, "value", None)
    if enum_name is not None and enum_value is not None:
        return {
            "name": str(enum_name),
            "value": normalize_json(enum_value),
        }

    return str(value)


def humanize(identifier: str) -> str:
    text = identifier.replace("-", " ").replace("_", " ")
    text = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", " ", text)
    return " ".join(word.capitalize() for word in text.split())


def collect_prefixed_values(option_type: type, prefix: str) -> dict[str, object]:
    """Collect option_/alias_ declarations across the inheritance chain."""
    result: dict[str, object] = {}
    for current_type in reversed(option_type.__mro__):
        for attribute_name, value in current_type.__dict__.items():
            if attribute_name.startswith(prefix):
                result[attribute_name[len(prefix):]] = value
    return result


def choice_metadata(option_type: type) -> tuple[list[dict], list[dict]]:
    canonical_values = collect_prefixed_values(option_type, "option_")
    alias_values = collect_prefixed_values(option_type, "alias_")

    choices = [
        {
            "name": name,
            "display_name": humanize(name),
            "value": normalize_json(value),
        }
        for name, value in canonical_values.items()
    ]
    choices.sort(key=lambda choice: (str(choice["value"]), choice["name"].casefold()))

    aliases = [
        {
            "name": name,
            "display_name": humanize(name),
            "value": normalize_json(value),
        }
        for name, value in alias_values.items()
    ]
    aliases.sort(key=lambda alias: (str(alias["value"]), alias["name"].casefold()))

    return choices, aliases


def get_option_kind(option_type: type) -> str:
    # Test the more-specific classes before their parent classes.
    if is_subclass(option_type, "Toggle"):
        return "toggle"
    if is_subclass(option_type, "TextChoice"):
        return "text_choice"
    if is_subclass(option_type, "NamedRange"):
        return "named_range"
    if is_subclass(option_type, "Range"):
        return "range"
    if is_subclass(option_type, "Choice"):
        return "choice"
    if is_subclass(option_type, "OptionCounter"):
        return "counter"
    if is_subclass(option_type, "OptionSet"):
        return "set"
    if is_subclass(option_type, "OptionList"):
        return "list"
    if is_subclass(option_type, "OptionDict"):
        return "dictionary"
    if is_subclass(option_type, "FreeText"):
        return "text"
    if is_subclass(option_type, "NumericOption"):
        return "number"
    if is_subclass(option_type, "Option"):
        return "option"
    return "unknown"


def serialize_visibility(option_type: type):
    visibility = getattr(option_type, "visibility", None)
    if visibility is None:
        return None

    name = getattr(visibility, "name", None)
    try:
        numeric_value = int(visibility)
    except (TypeError, ValueError):
        numeric_value = None

    flags: list[str] = []
    members = getattr(type(visibility), "__members__", {})
    if numeric_value is not None:
        for member_name, member in members.items():
            try:
                member_value = int(member)
            except (TypeError, ValueError):
                continue

            if member_value == 0:
                if numeric_value == 0:
                    flags.append(member_name)
            elif numeric_value & member_value == member_value:
                flags.append(member_name)

    return {
        "name": str(name) if name is not None else str(visibility),
        "value": numeric_value if numeric_value is not None else normalize_json(visibility),
        "flags": flags,
    }


def yaml_default(option_type: type, kind: str, raw_default, choices: list[dict]):
    """Return the value the Vue app should normally write into generated YAML."""
    if kind == "toggle":
        return bool(raw_default)

    if kind in ("choice", "text_choice"):
        if isinstance(raw_default, str):
            return raw_default

        for choice in choices:
            if choice["value"] == normalize_json(raw_default):
                return choice["name"]

    return normalize_json(raw_default)


def serialize_option(key: str, option_type: type, is_game_option: bool) -> dict:
    kind = get_option_kind(option_type)
    raw_default = getattr(option_type, "default", None)
    choices, aliases = choice_metadata(option_type)

    option_data = {
        "key": key,
        "class_name": option_type.__name__,
        "module": option_type.__module__,
        "kind": kind,
        "display_name": getattr(option_type, "display_name", None) or humanize(key),
        "description": inspect.getdoc(option_type) or "",
        "default": yaml_default(option_type, kind, raw_default, choices),
        "raw_default": normalize_json(raw_default),
        "visibility": serialize_visibility(option_type),
        "source": "game" if is_game_option else "archipelago_common",
    }

    if choices:
        option_data["choices"] = choices
    if aliases:
        option_data["aliases"] = aliases

    if kind in ("range", "named_range"):
        option_data["minimum"] = normalize_json(getattr(option_type, "range_start", None))
        option_data["maximum"] = normalize_json(getattr(option_type, "range_end", None))

        special_values = getattr(option_type, "special_range_names", None)
        if special_values:
            option_data["special_values"] = normalize_json(special_values)

    if kind in ("set", "counter", "list"):
        valid_keys = getattr(option_type, "valid_keys", None)
        if valid_keys:
            option_data["valid_keys"] = normalize_json(valid_keys)

        if hasattr(option_type, "valid_keys_casefold"):
            option_data["valid_keys_casefold"] = bool(option_type.valid_keys_casefold)

        # An OptionSet/OptionList with no valid_keys can accept arbitrary strings.
        option_data["allow_custom_values"] = not bool(valid_keys)

    if kind == "counter":
        option_data["minimum_value"] = normalize_json(getattr(option_type, "min", None))

    if kind == "text_choice":
        option_data["allow_custom_values"] = True

    if kind == "dictionary":
        option_data["has_validation_schema"] = getattr(option_type, "schema", None) is not None

    if hasattr(option_type, "supports_weighting"):
        option_data["supports_weighting"] = bool(option_type.supports_weighting)

    return option_data


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source_file:
        for chunk in iter(lambda: source_file.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


options_class = getattr(options_module, "Spire2Options")
web_world_class = getattr(web_world_module, "SlayTheSpire2Web")

type_hint_globals = dict(vars(ap_options))
type_hint_globals.update(vars(options_module))

try:
    resolved_type_hints = get_type_hints(
        options_class,
        globalns=type_hint_globals,
        localns=type_hint_globals,
    )
except Exception as error:
    print(
        f"Warning: Could not fully resolve type hints ({error}); using dataclass field types.",
        file=sys.stderr,
    )
    resolved_type_hints = {}

local_annotations = set(getattr(options_class, "__annotations__", {}).keys())
option_fields: list[tuple[str, type]] = []
for field in fields(options_class):
    option_type = resolved_type_hints.get(field.name, field.type)
    if isinstance(option_type, str):
        option_type = type_hint_globals.get(option_type, option_type)
    if not isinstance(option_type, type):
        raise TypeError(
            f"Option field '{field.name}' resolved to {option_type!r}, not an option class."
        )
    option_fields.append((field.name, option_type))

option_order = [key for key, _ in option_fields]
keys_by_type: dict[type, list[str]] = {}
for key, option_type in option_fields:
    keys_by_type.setdefault(option_type, []).append(key)

compiled_options = {
    key: serialize_option(key, option_type, key in local_annotations)
    for key, option_type in option_fields
}

compiled_groups: list[dict] = []
grouped_keys: set[str] = set()
for group in getattr(web_world_class, "option_groups", []):
    group_name = getattr(group, "name", None) or getattr(group, "display_name", None)
    if not group_name:
        raise ValueError(f"Encountered an option group without a name: {group!r}")

    group_keys: list[str] = []
    for grouped_option_type in getattr(group, "options", []):
        matching_keys = keys_by_type.get(grouped_option_type, [])
        if not matching_keys:
            print(
                f"Warning: '{grouped_option_type.__name__}' appears in web_world.py "
                "but is not a field on Spire2Options.",
                file=sys.stderr,
            )
            continue

        for key in matching_keys:
            if key in grouped_keys:
                print(
                    f"Warning: Option '{key}' appears in more than one web option group; "
                    "keeping its first group.",
                    file=sys.stderr,
                )
                continue
            group_keys.append(key)
            grouped_keys.add(key)

    if group_keys:
        compiled_groups.append(
            {
                "name": str(group_name),
                "option_keys": group_keys,
                "start_collapsed": bool(getattr(group, "start_collapsed", False)),
                "source": "web_world",
            }
        )

# web_world.py intentionally groups only selected options. Keep every remaining
# dataclass field available to the Vue app instead of silently omitting it.
ungrouped_game_keys = [
    key for key in option_order
    if key not in grouped_keys and key in local_annotations
]
ungrouped_common_keys = [
    key for key in option_order
    if key not in grouped_keys and key not in local_annotations
]

if ungrouped_game_keys:
    compiled_groups.append(
        {
            "name": "Other Game Options",
            "option_keys": ungrouped_game_keys,
            "start_collapsed": True,
            "source": "generated_fallback",
        }
    )
    grouped_keys.update(ungrouped_game_keys)

if ungrouped_common_keys:
    compiled_groups.append(
        {
            "name": "Common Archipelago Options",
            "option_keys": ungrouped_common_keys,
            "start_collapsed": True,
            "source": "generated_fallback",
        }
    )
    grouped_keys.update(ungrouped_common_keys)

for group in compiled_groups:
    for key in group["option_keys"]:
        compiled_options[key]["group"] = group["name"]

missing_keys = [key for key in option_order if key not in grouped_keys]
if missing_keys:
    raise RuntimeError(
        "The following options were not included in any output group: "
        + ", ".join(missing_keys)
    )

compiled = {
    "schema_version": 1,
    "game": "Slay the Spire 2",
    "sources": {
        "options": {
            "path": "world/spire2/options.py",
            "sha256": file_sha256(options_path),
        },
        "web_world": {
            "path": "world/spire2/web_world.py",
            "sha256": file_sha256(web_world_path),
        },
    },
    "option_order": option_order,
    "groups": compiled_groups,
    "options": compiled_options,
}

out_path.parent.mkdir(parents=True, exist_ok=True)
temporary_path = out_path.with_suffix(out_path.suffix + ".tmp")
temporary_path.write_text(
    json.dumps(compiled, indent=2, ensure_ascii=False) + "\n",
    encoding="utf-8",
)
temporary_path.replace(out_path)

print(
    f"Compiled {len(compiled_options)} options in {len(compiled_groups)} groups "
    f"to {out_path}"
)
'@

$py | & $PythonExecutable - $RepoRoot $OutPath $ArchipelagoRoot

if ($LASTEXITCODE -ne 0) {
    throw "Python option generation failed with exit code $LASTEXITCODE"
}

if (Test-Path $OutPath -PathType Leaf) {
    Write-Host "Wrote Vue option metadata to $OutPath"
} else {
    throw "Python completed without producing $OutPath"
}
