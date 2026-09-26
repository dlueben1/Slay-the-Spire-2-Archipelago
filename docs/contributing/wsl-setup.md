# WSL development

Keep this repository and an Archipelago 0.6.7 checkout beside each other. Build in WSL or Windows; run Slay the Spire 2 on Windows. The APWorld source lives at `world/spire2` and is linked into Archipelago so edits are available immediately.

## Python and the APWorld

Install `uv`, then run these commands from the repository root:

```bash
git clone --depth 1 --branch 0.6.7 https://github.com/ArchipelagoMW/Archipelago.git ../Archipelago
uv python install 3.13
uv venv --python 3.13 --seed .venv
.venv/bin/python ../Archipelago/ModuleUpdate.py -y
ln -s "$PWD/world/spire2" ../Archipelago/worlds/spire2
site_packages=$(.venv/bin/python -c 'import sysconfig; print(sysconfig.get_paths()["purelib"])')
realpath ../Archipelago > "$site_packages/archipelago.pth"
```

If you already have the Archipelago checkout or world link, keep those and skip the corresponding commands. Select `<repo>/.venv/bin/python` as the WSL interpreter in your Python IDE. The `.pth` file makes `worlds.*` imports available even when you open `world/` directly. If the IDE still cannot resolve them after reindexing, add the Archipelago checkout as a source or interpreter path.

Build the world with `.venv/bin/python scripts/build_apworld_local.py`. The output is `dist/spire2.apworld`.

## Game assemblies for client development

To compile against your installed game instead of the [reference assemblies](sts2-api-compat.md), get **only these three files** from the game's `data_sts2_windows_x86_64` directory for each API version you want to inspect:

- `sts2.dll`
- `0Harmony.dll`
- `GodotSharp.dll`

Place them in ignored `.local-tools/sts2-references/` with this layout:

```text
.local-tools/sts2-references/
├── 0.107.1/             # public game version
│   ├── sts2.dll
│   ├── 0Harmony.dll
│   └── GodotSharp.dll
└── 0.111.0/             # beta game version
    ├── sts2.dll
    ├── 0Harmony.dll
    └── GodotSharp.dll
```

After switching Steam to the version you want, copy the files from that installation's `Slay the Spire 2/data_sts2_windows_x86_64/` directory. For example, from the repository root in WSL:

```bash
game_data_dir="/path/to/Slay the Spire 2/data_sts2_windows_x86_64"
api_version="0.111.0"  # use 0.107.1 for the public version
mkdir -p ".local-tools/sts2-references/$api_version"
cp "$game_data_dir/sts2.dll" "$game_data_dir/0Harmony.dll" "$game_data_dir/GodotSharp.dll" \
  ".local-tools/sts2-references/$api_version/"
```

Repeat for the other version if you have it. `Sts2ApiSignatureRoot` points to `.local-tools/sts2-references`; the project appends `Sts2ApiCompat` and reads those three DLLs from the matching version directory. If you do not have both game versions, set `UseSts2RefLib=true` in `local.props` to use the versioned NuGet reference assemblies for compatibility builds.

## Client and Godot

Install the .NET 10 SDK and the Linux x86_64 **.NET** Godot 4.5.1 editor in WSL. For example, download Godot from the [official release](https://github.com/godotengine/godot-builds/releases/tag/4.5.1-stable):

```bash
mkdir -p .local-tools/godot
curl -fL --retry 3 -o .local-tools/godot/Godot_v4.5.1-stable_mono_linux_x86_64.zip \
  https://github.com/godotengine/godot-builds/releases/download/4.5.1-stable/Godot_v4.5.1-stable_mono_linux_x86_64.zip
unzip -q .local-tools/godot/Godot_v4.5.1-stable_mono_linux_x86_64.zip -d .local-tools/godot
rm .local-tools/godot/Godot_v4.5.1-stable_mono_linux_x86_64.zip
```

Copy `client/StS2AP/local.props.template` to ignored `client/StS2AP/local.props` and configure these WSL build properties. Set `STS2GamePath` to your Steam installation if you also use local launch or deployment from the IDE.

```xml
<Project>
  <PropertyGroup>
    <Sts2ApiCompat>0.111.0</Sts2ApiCompat>
    <UseSts2RefLib>false</UseSts2RefLib>
    <Sts2ApiSignatureRoot>$(MSBuildThisFileDirectory)../../.local-tools/sts2-references</Sts2ApiSignatureRoot>
    <GodotExePath>$(MSBuildThisFileDirectory)../../.local-tools/godot/Godot_v4.5.1-stable_mono_linux_x86_64/Godot_v4.5.1-stable_mono_linux.x86_64</GodotExePath>
    <PythonExe>$(MSBuildThisFileDirectory)../../.venv/bin/python</PythonExe>
    <BuildMode>CompileOnly</BuildMode>
    <ModsOutputDir>$(MSBuildThisFileDirectory)../../dist/Archipelago</ModsOutputDir>
  </PropertyGroup>
</Project>
```

If you are using the NuGet reference assemblies, set `UseSts2RefLib` to `true` and omit `Sts2ApiSignatureRoot`. Change `Sts2ApiCompat` and reload your IDE project to inspect another API. Rider or another .NET IDE can open `client/StS2AP/StS2AP.csproj`; an IDE with WSL interpreter support can use the Python environment above.

Check both APIs and build the package in WSL:

```bash
dotnet build client/StS2AP/StS2AP.csproj -c Release -p:Sts2ApiCompat=0.107.1 -p:BuildMode=CompileOnly
dotnet build client/StS2AP/StS2AP.csproj -c Release -p:Sts2ApiCompat=0.111.0 -p:BuildMode=CompileOnly
dotnet build client/StS2AP/StS2AP.csproj -c Release -p:Sts2ApiCompat=0.111.0 -p:BuildMode=Package
```

The package is written to `dist/Archipelago/`, ready to install in the Windows game's `mods` directory. If Godot reports missing shared libraries on a minimal WSL installation, install your distribution's `fontconfig`, `freetype`, and `libpng` packages.
