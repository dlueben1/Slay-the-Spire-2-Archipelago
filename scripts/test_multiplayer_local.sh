#!/usr/bin/env bash
# Start the Windows multiplayer test launcher from WSL.
set -euo pipefail

if ! command -v powershell.exe >/dev/null 2>&1; then
  echo 'Windows PowerShell is unavailable from this WSL shell.' >&2
  exit 1
fi
if ! command -v wslpath >/dev/null 2>&1; then
  echo 'wslpath is required to translate paths for Windows PowerShell.' >&2
  exit 1
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
script_path="$(wslpath -w "$script_dir/test_multiplayer_local.ps1")"

args=()
while (( $# > 0 )); do
  if [[ "$1" == -ExePath ]]; then
    if (( $# < 2 )); then
      echo 'Missing path after -ExePath.' >&2
      exit 2
    fi
    exe_path="$2"
    if [[ "$exe_path" == /* ]]; then
      exe_path="$(wslpath -w "$exe_path")"
    fi
    args+=("$1" "$exe_path")
    shift 2
  else
    args+=("$1")
    shift
  fi
done

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$script_path" "${args[@]}"
