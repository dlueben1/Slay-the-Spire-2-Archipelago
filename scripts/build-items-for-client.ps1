$ErrorActionPreference = "Stop"

$repoRoot    = Resolve-Path "$PSScriptRoot\.."
$itemsPyPath = Join-Path $repoRoot "world\items.py"
$csPath      = Join-Path $repoRoot "client\StS2AP\Data\ItemTable.cs"

# Write a temp Python script that parses items.py via AST (no Archipelago install needed)
$tempScript = [System.IO.Path]::GetTempFileName() -replace '\.tmp$', '.py'

try {
    Set-Content -Path $tempScript -Encoding UTF8 -Value @'
import ast, json, sys

with open(sys.argv[1], 'r') as f:
    tree = ast.parse(f.read())

start_id = None
item_names = []

for node in tree.body:
    if isinstance(node, ast.Assign):
        for target in node.targets:
            if isinstance(target, ast.Name) and target.id == 'START_ID':
                v = node.value
                start_id = v.value if hasattr(v, 'value') else v.n
    elif isinstance(node, ast.AnnAssign):
        if isinstance(node.target, ast.Name) and node.target.id == 'item_table':
            if isinstance(node.value, ast.Dict):
                for key in node.value.keys:
                    if isinstance(key, ast.Constant):
                        item_names.append(key.value)

print(json.dumps({"start_id": start_id, "items": item_names}))
'@

    Write-Host "Extracting item table from $itemsPyPath..."
    $jsonOutput = & python $tempScript $itemsPyPath
    if ($LASTEXITCODE -ne 0) {
        throw "Python extraction script failed with exit code $LASTEXITCODE."
    }

    $data    = $jsonOutput | ConvertFrom-Json
    $startId = [int]$data.start_id
    $items   = @($data.items)

    if ($items.Count -eq 0) {
        throw "No items found in item_table. Check that items.py is structured correctly."
    }

    Write-Host "Found $($items.Count) items with START_ID=$startId."

    # Converts an item name to a valid C# PascalCase identifier.
    # Removes spaces, capitalises each word, and prefixes with _ if the result starts with a digit.
    function ConvertTo-CsIdentifier ([string]$name) {
        $pascal = ($name -split '\s+' | ForEach-Object {
            if ($_ -match '^\d') { $_ } else { $_.Substring(0,1).ToUpper() + $_.Substring(1) }
        }) -join ''
        if ($pascal -match '^\d') { "_$pascal" } else { $pascal }
    }

    # Build C# enum entries and dictionary entries: key = START_ID + i (enumerate start=1)
    $enumLines = for ($i = 0; $i -lt $items.Count; $i++) {
        $id    = $startId + $i + 1
        $ident = ConvertTo-CsIdentifier $items[$i]
        "            $ident = $id"
    }
    $dictLines = for ($i = 0; $i -lt $items.Count; $i++) {
        $id   = $startId + $i + 1
        $name = $items[$i] -replace '"', '\"'   # escape any double-quotes in item names
        "            { $id, `"$name`" }"
    }

    $enumEntriesStr = $enumLines -join ",`r`n"
    $dictEntriesStr = $dictLines -join ",`r`n"

    # Read the existing C# file and extract the preamble (using statements + namespace opening)
    $cs = Get-Content -Path $csPath -Raw -Encoding UTF8

    # Capture everything up to and including the opening brace of ItemTable class
    if ($cs -notmatch '(?s)(.*?public static class ItemTable\s*\{)') {
        throw "Could not find 'public static class ItemTable' in $csPath."
    }
    $preamble = $Matches[1]

    # Capture the namespace closing brace(s) that follow the class body
    # We need the final two closing braces (class + namespace)
    $suffix = "`r`n    }`r`n}"

    # Build the full regenerated file
    $nl = "`r`n"
    $newContent  = $preamble + $nl
    $newContent += "        public enum APItem" + $nl
    $newContent += "        {" + $nl
    $newContent += $enumEntriesStr + $nl
    $newContent += "        }" + $nl
    $newContent += $nl
    $newContent += "        public static Dictionary<int, string> Items = new Dictionary<int, string>" + $nl
    $newContent += "        {" + $nl
    $newContent += $dictEntriesStr + $nl
    $newContent += "        };" + $nl
    $newContent += $suffix

    Set-Content -Path $csPath -Value $newContent -Encoding UTF8 -NoNewline

    $firstId = $startId + 1
    $lastId  = $startId + $items.Count
    Write-Host "Updated: $csPath"
    Write-Host "  Items: $($items.Count)  |  ID range: $firstId..$lastId"

} finally {
    if (Test-Path $tempScript) {
        Remove-Item $tempScript -Force
    }
}
