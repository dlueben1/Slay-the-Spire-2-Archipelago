# Building the Projects

## Prerequisites

You need the following installed:

- Slay the Spire 2
- Godot v4.5.1 (the .NET version, _NOT_ the standalone version and _NOT_ the Megadot version)
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

## Building the APWorld

Copy the folder inside `world` into your local Archipelago Installation's `world` folder, then open the launcher and select "Build APWorlds", and you'll find it in the `build` folder

> [!WARNING]
> If you need to update `ItemTable.cs` because you've changed the items in the APWorld, run `./scripts/generate_item_enums.ps1`. This will cause many errors but can be helpful if a large change was made.

## Building the Game Client

Use `Ctrl+Shift+B` or `Build > Build Solution`. This will automatically place the mod in the `mods` folder of Slay the Spire

> [!CAUTION]
> Note that while this is a Godot game, and Godot is needed for _building_ the mod, I don't know how to use the Godot Engine itself to modify/view any game files. All of this is handled entirely in code in Visual Studio.

> [!TIP]
> While developing the mod, use `Ctrl+Alt+J` to view the "Object Browser", this will let you view the `sts2` namespace and all of the source code for the game

# Submitting a Change

The main workflow for submitting a change to the mod is to fork the repo and then open a Pull Request. From there, one of the project's collaborators will review and approve/deny your PR.
