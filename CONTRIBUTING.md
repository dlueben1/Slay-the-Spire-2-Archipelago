# Building the Projects

Developing from WSL? See [WSL development setup](docs/contributing/wsl-setup.md) for the tested Archipelago, Python, Godot, and dual-API client workflow.

## Prerequisites

You need the following installed:

- Slay the Spire 2
- Godot v4.5.1 (the .NET version, _NOT_ the standalone version and _NOT_ the Megadot version)
- A code editor of your choice; C#/.NET and Python support are useful for navigating both projects
- .NET 10 SDK (the client still targets `net9.0`)

You also need to:

- Clone Archipelago 0.6.7 in `../Archipelago` and make `world/spire2` available in its `worlds/spire2` directory by copying or linking it; see the [WSL setup](docs/contributing/wsl-setup.md)
- Copy `client/StS2AP/local.props.template` to `client/StS2AP/local.props` and update its paths to match your local installations
  - `<STS2GamePath>` should point to the directory for the game in Steam
  - Keep `<UseSts2RefLib>true</UseSts2RefLib>` for portable, permissioned compile-time references from NuGet
  - Optionally set `<UseSts2RefLib>false</UseSts2RefLib>` and configure `<Sts2ApiSignatureRoot>` to compile against DLLs extracted from your own game installations
  - `<GodotExePath>` should point to the Godot 4.5.1 .NET executable for the OS that runs the build
  - See [Selecting an STS2 API for development](docs/contributing/sts2-api-compat.md) for configuring your editor and compatibility builds

> [!CAUTION]
> For the moment this mod only supports Windows, primarily because of the way I'm handling real-time logging for the purpose of debugging the app. This should not be the case in the future.

## Building the APWorld

Choose whichever build method fits your setup:

- Copy `world/spire2` into `../Archipelago/worlds/spire2`, then select **Build APWorlds** in the Archipelago launcher. The file appears at `../Archipelago/build/apworlds/spire2.apworld`.
- On Windows, run `.\scripts\build_world.ps1` in PowerShell. It replaces the copied world in the sibling Archipelago checkout, runs the launcher build, and copies the result to `dist/spire2.apworld`. Use this with a copied world, not a linked one.
- With `world/spire2` linked into `../Archipelago/worlds/spire2`, run `.venv/bin/python scripts/build_apworld_local.py` from WSL. The result is `dist/spire2.apworld`.

> [!WARNING]
> If you need to update `ItemTable.cs` because you've changed the items in the APWorld, run `./scripts/generate_item_enums.ps1`. This will cause many errors but can be helpful if a large change was made.

## Building the Game Client

Build `client/StS2AP/StS2AP.csproj` with `dotnet build` or your editor's build command. `BuildMode=CompileOnly` checks compilation without deploying. `BuildMode=Package` writes a package to `dist/Archipelago` when `ModsOutputDir` is configured as in the WSL setup.

> [!CAUTION]
> Godot is needed to export the mod package. The client source is C# and can be edited with any suitable editor.

> [!TIP]
> For API navigation and completion in an editor, use the matching game assemblies or NuGet reference assemblies described in the [WSL setup](docs/contributing/wsl-setup.md). Reload the project after changing `Sts2ApiCompat`.

## Testing Multiplayer Locally

Run `scripts/test_multiplayer_local.ps1` from Windows PowerShell, or `./scripts/test_multiplayer_local.sh` from WSL to launch the Windows game. See the [script instructions](scripts/README.md#local-multiplayer-test-from-wsl) for setup and options.

# Submitting a Change

The main workflow for submitting a change to the mod is to fork the repo and then open a Pull Request. From there, one of the project's collaborators will review and approve/deny your PR.
