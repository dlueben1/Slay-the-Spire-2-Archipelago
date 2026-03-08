# Slay the Spire 2 Archipelago

Archipelago Client & APWorld for STS2

# Prerequisites

You need the following installed:

- Slay the Spire 2
- Godot v4.5.1 (the .NET version, _NOT_ the standalone version)
- Visual Studio 2022 (for the Game Client / C# part)
- Visual Studio Code (for the AP World)
- .NET 9

You also need to:

- Create a clone of Archipelago's repo should live in `../Archipelago` or Python will complain about the APWorld's `.py` files
- Copy `client/StS2AP/local.props.template` to `client/StS2AP/local.props` and update `<STS2GamePath>` and `<GodotExePath>` to match your local installations
  - `<STS2GamePath>` should point to the directory for the game in Steam
  - `<GodotExePath>` should point to the Godot Directory that has `Godot_v4.5.1-stable_mono_win64.exe`

> [!CAUTION]
> For the moment this mod only supports Windows, primarily because of the way I'm handling real-time logging for the purpose of debugging the app. This should not be the case in the future.

# Building the Projects

## Building the APWorld

Run `./scripts/build-world.ps1` to generate the `.apworld` file. It should be in `dist/slaythespire2.apworld`

## Building the Game Client

Use `Ctrl+Shift+B` or `Build > Build Solution`. This will automatically place the mod in the `mods` folder of Slay the Spire

# Special Thanks

- [Archipelago.MultiClient.Net](https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net)
- [lamali292](https://github.com/lamali292) for their initial guide on creating mods for Slay the Spire 2!
- [alwaysintreble](https://github.com/alwaysintreble/ArchipelagoBepInExPluginTemplate/tree/master) for their example Archipelago C# Plugin Template
