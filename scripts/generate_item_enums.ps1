<#
Generate C# enums from the runtime Python world code under world/spire2.
Writes client/StS2AP/Data/ItemTable.cs (backs up existing file).
Run: powershell -ExecutionPolicy Bypass -File .\scripts\generate_item_enums.ps1
#>

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..") | Select-Object -ExpandProperty Path
$OutPath = Join-Path $RepoRoot "client\StS2AP\Data\ItemTable.cs"
$BackupPath = $OutPath + ".bak"

$py = @'
import sys
import importlib.util
from pathlib import Path
import re

repo_root = Path(sys.argv[1])
world_dir = repo_root / "world" / "spire2"

def load_module_as(name, path):
    spec = importlib.util.spec_from_file_location(name, str(path))
    mod = importlib.util.module_from_spec(spec)
    sys.modules[name] = mod
    spec.loader.exec_module(mod)
    return mod

# load characters and constants first so items' absolute imports work
chars = load_module_as("worlds.spire2.characters", world_dir / "characters.py")
consts = load_module_as("worlds.spire2.constants", world_dir / "constants.py")
items = load_module_as("worlds.spire2.items", world_dir / "items.py")

CHAR_OFFSET = int(getattr(consts, "CHAR_OFFSET", 10000))
character_list = list(getattr(chars, "character_list", []))

# collect raw items (base + universal) with non-None codes
raw_entries = []
# base_item_table and universal_items are typed dicts of name->ItemData
for src in ("universal_items", "base_item_table"):
    table = getattr(items, src, {})
    if table:
        for name, itemdata in table.items():
            code = getattr(itemdata, "code", None)
            if code is not None:
                raw_entries.append((name, int(code)))

# sanitize C# enum member names, ensure uniqueness
def sanitize(name):
    s = re.sub(r'[^0-9A-Za-z]', '', name)
    if re.match(r'^[0-9]', s):
        s = "_" + s
    if not s:
        s = "Item"
    return s

seen = {}
members = []
for name, code in raw_entries:
    base = sanitize(name)
    candidate = base
    i = 2
    while candidate in seen:
        candidate = f"{base}_{i}"
        i += 1
    seen[candidate] = code
    members.append((candidate, code, name))

# produce RawCharacterID members
char_members = []
for idx, cname in enumerate(character_list, start=1):
    member = sanitize(cname)
    value = idx * CHAR_OFFSET
    char_members.append((member, value, cname))

# format C# file
ns = "StS2AP.Data"
lines = []
lines.append("namespace " + ns)
lines.append("{")
lines.append("    public enum RawItemID")
lines.append("    {")
# sort by numeric then name for stable output
for name, code, orig in sorted(members, key=lambda x: (x[1], x[0].lower())):
    lines.append(f"        {name} = {code}, // {orig}")
lines.append("    }")
lines.append("")
lines.append("    public enum RawCharacterID")
lines.append("    {")
for name, value, orig in char_members:
    lines.append(f"        {name} = {value}, // {orig}")
lines.append("    }")
lines.append("}")
out_text = "\n".join(lines)

# write to stdout for debugging and write to file path arg
out_path = Path(sys.argv[2])
print(out_text)
out_path.write_text(out_text, encoding="utf-8")
'@

# run python with repo root and output path as args
$py | python - $RepoRoot $OutPath

if (Test-Path $OutPath) {
    if (-not (Test-Path $BackupPath)) {
        Copy-Item $OutPath $BackupPath -Force
    }
    Write-Host "Wrote enums to $OutPath (backup at $BackupPath)"
} else {
    Write-Error "Failed to produce $OutPath"
}